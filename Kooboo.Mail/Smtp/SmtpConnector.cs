//Copyright (c) 2018 Yardi Technology Limited. Http://www.kooboo.com 
//All rights reserved.
using System;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kooboo.Mail.Utility;

namespace Kooboo.Mail.Smtp
{
    public class SmtpConnector : IDisposable
    {
        private TcpClient _client;
        private Stream _stream;
        private PipeReader _reader;
        private PipeWriter _writer;
        private SmtpServer _server;

        private CancellationTokenSource _cancellationTokenSource;

        public SmtpConnector(SmtpServer server, TcpClient client)
        {
            _server = server;
            _client = client;
            Local = CopyIPEndPoint(_client.Client.LocalEndPoint as IPEndPoint);
            Client = CopyIPEndPoint(_client.Client.RemoteEndPoint as IPEndPoint);
        }

        public IPEndPoint Local { get; set; }

        public IPEndPoint Client { get; set; }

        public async Task Accept()
        {
            // Add cancellation token to allow cancel from any point calling Dispose()
            _cancellationTokenSource = new CancellationTokenSource();

            int MailCounter = 0;
            string resolvedServerHostName = null;

            try
            {
                _stream = _client.GetStream();

                if (_server.SSL)
                {
                    var ssl = new SslStream(_stream, false);
                    var options = new SslServerAuthenticationOptions
                    {
                        ServerCertificateSelectionCallback = (sender, hostName) =>
                        {
                            if (string.IsNullOrEmpty(hostName))
                            {
                                hostName = this.Local.Port == 587 ? Settings.Port587SmtpDomain : Settings.SmtpDomain;
                            }
                            else
                            {
                                resolvedServerHostName = hostName;
                            }
                            var cert2 = Settings.LoadCertificateFromFile(hostName) ?? Kooboo.Data.SSL.SslCertificateProvider.SelectCertificate2(hostName);
                            if (cert2 == null)
                            {
                                cert2 = Settings.LoadCertificateFromFile(this.Local.Port == 587 ? Settings.Port587SmtpDomain : Settings.SmtpDomain) ?? 
                                        Kooboo.Data.SSL.SslCertificateProvider.SelectCertificate2(this.Local.Port == 587 ? Settings.Port587SmtpDomain : Settings.SmtpDomain);
                            }
                            return cert2;
                        },
                        ClientCertificateRequired = false,
                        EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
                    };

                    await ssl.AuthenticateAsServerAsync(options);
                    _stream = ssl;
                }

                _stream.ReadTimeout =
                _stream.WriteTimeout = _server.Timeout;

                _reader = PipeReader.Create(_stream);
                _writer = PipeWriter.Create(_stream);

                SmtpSession session = new SmtpSession(this.Client.Address);
                if (resolvedServerHostName != null)
                {
                    session.ServerHostName = resolvedServerHostName;
                }

                var securityResult = Kooboo.Mail.SecurityControl.Manager.Check(this.Client.Address);
                if (!securityResult.CanConnect)
                {
                    await WriteLineAsync("550 " + securityResult.Error);
                    Dispose();
                    return;
                }

                //Service ready
                await WriteLineAsync(session.ServiceReady().Render());
                var commandLine = await _reader.ReadLineAsync(Encoding.Default, TimeSpan.FromSeconds(30));

                MailLogger.WriteLine(_stream, commandLine + " : " + this.Client.Address.ToString(), "SMTP", true);

                var cancellationToken = _cancellationTokenSource.Token;

                while (!cancellationToken.IsCancellationRequested && commandLine != null)
                {
                    var response = session.Command(commandLine);

                    if (response.SendResponse)
                    {
                        var responseline = response.Render();
                        if (_stream is System.Net.Security.SslStream)
                        {
                            responseline = responseline.Replace("250-STARTTLS\r\n", "");
                        }
                        await WriteLineAsync(responseline);
                    }

                    if (response.StartTls)
                    {
                        var sslStream = new System.Net.Security.SslStream(_stream, false, ValidateCertificate);
                        var options = new SslServerAuthenticationOptions
                        {
                            ServerCertificateSelectionCallback = (sender, hostName) =>
                            {
                                if (string.IsNullOrEmpty(hostName))
                                {
                                    hostName = this.Local.Port == 587 ? Settings.Port587SmtpDomain : Settings.SmtpDomain;
                                }
                                else
                                {
                                    resolvedServerHostName = hostName;
                                }
                                var cert2 = Settings.LoadCertificateFromFile(hostName) ?? Kooboo.Data.SSL.SslCertificateProvider.SelectCertificate2(hostName);
                                if (cert2 == null)
                                {
                                    cert2 = Settings.LoadCertificateFromFile(this.Local.Port == 587 ? Settings.Port587SmtpDomain : Settings.SmtpDomain) ?? 
                                            Kooboo.Data.SSL.SslCertificateProvider.SelectCertificate2(this.Local.Port == 587 ? Settings.Port587SmtpDomain : Settings.SmtpDomain);
                                }
                                return cert2;
                            },
                            ClientCertificateRequired = false,
                            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
                        };

                        sslStream.AuthenticateAsServer(options);

                        _stream = sslStream;
                        _reader = PipeReader.Create(_stream);
                        _writer = PipeWriter.Create(_stream);
                        session.ReSet();
                        session.State = SmtpSession.CommandState.Helo;
                        if (resolvedServerHostName != null)
                        {
                            session.ServerHostName = resolvedServerHostName;
                        }
                    }

                    if (response.SessionCompleted)
                    {
                        await Kooboo.Mail.Transport.Incoming.Receive(session);
                    }

                    if (response.Close)
                    {
                        Dispose();
                        break;
                    }

                    // When enter the session state, read till the end . 
                    if (session.State == SmtpSession.CommandState.Data)
                    {
                        var externalto = AddressUtility.GetExternalRcpts(session);
                        var counter = externalto.Count();

                        if (counter > 0)
                        {
                            if (!Kooboo.Data.Infrastructure.InfraManager.Test(session.OrganizationId, Data.Infrastructure.InfraType.Email, counter))
                            {
                                await WriteLineAsync("550 you have no enough credit to send emails");
                                Dispose();
                                break;
                            }
                        }

                        var data = await _reader.ReadToDotAsync(TimeSpan.FromMinutes(8));
                        if (data == null) break;

                        MailLogger.WriteLine(_stream, data, "SMTP", true);

                        var dataresponse = await session.Data(data);

                        // after data, can check the result... TODO: when all 3 failed, should return...
                        //if (session.TestResult !=null && session.TestResult.AllFailed)
                        //{ 
                        //}


                        if (dataresponse.SendResponse)
                        {
                            await WriteLineAsync(dataresponse.Render());
                        }

                        if (dataresponse.SessionCompleted)
                        {
                            await Kooboo.Mail.Transport.Incoming.Receive(session);

                            var tos = session.Log.Keys.Where(o => o.Name == SmtpCommandName.RCPTTO);

                            string subject = "TO: ";
                            if (tos != null)
                            {
                                foreach (var item in tos)
                                {
                                    if (item != null && !string.IsNullOrWhiteSpace(item.Value))
                                    {
                                        subject += item.Value;
                                    }
                                }
                            }

                            if (counter > 0)
                            {
                                Kooboo.Data.Infrastructure.InfraManager.Add(session.OrganizationId, Data.Infrastructure.InfraType.Email, counter, subject);
                            }

                            session.ReSet();

                            MailCounter += 1;
                        }

                        if (dataresponse.Close)
                        {
                            Dispose();
                        }
                    }

                    if (MailCounter > _server.Options.MailsPerConnection)
                    {
                        await this.WriteLineAsync("550 Max mails per connection reached");
                        Dispose();
                        return;
                    }

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        commandLine = await _reader.ReadLineAsync(Encoding.Default, TimeSpan.FromSeconds(30));
                        MailLogger.WriteLine(_stream, commandLine, "SMTP", true);
                    }
                }

            }
            catch (ObjectDisposedException)
            {
                // Caused by our active connection closing, no need to handle as exception
            }
            catch (SocketException ex)
            {
                Kooboo.Data.Log.Instance.Exception.Write(ex.ToString());
            }
            catch (TimeoutException ex)
            {
                Kooboo.Data.Log.Instance.Exception.Write(ex.ToString());
            }
            catch (InvalidDataException)
            {
                //maybe client clost connection.
            }
            catch (Exception ex)
            {
                try
                {
                    if (_client.Connected)
                    {
                        await WriteLineAsync("550 Internal Server Error");
                    }
                }
                catch
                {
                }
                Kooboo.Data.Log.Instance.Exception.Write(ex.Message.ToString());
            }
            finally
            {
                Dispose();
            }
        }

        private bool ValidateCertificate(object s, X509Certificate c, X509Chain h, SslPolicyErrors p) => true;

        private async Task WriteLineAsync(string line)
        {
            MailLogger.WriteLine(_stream, line, $"SMTP", false);
            // UTF-8 is a strict ASCII superset; required for RFC 6531 responses that
            // echo internationalized addresses (previously Unicode became '?').
            await _writer.WriteLineAsync(line, Encoding.UTF8, 30);
            await _writer.FlushAsync();
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _client?.Close();
        }

        private IPEndPoint CopyIPEndPoint(IPEndPoint ip)
        {
            return new IPEndPoint(ip.Address, ip.Port);
        }
    }
}
