using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Kooboo.Mail.Models;

namespace Kooboo.Mail
{
    public static class Settings
    {
        static Settings()
        {

        }

        public static async Task<SendSetting> GetSendSetting(bool IsOnlineServer, string mailFrom, string RCPTTO)
        {
            SendSetting setting = new();
            //#if DEBUG
            //            setting.KoobooServerIp = "127.0.0.1";
            //            // setting.KoobooServerIp = "mta3.kmailserver.com";
            //            setting.OkToSend = true;
            //            setting.HostName = System.Net.Dns.GetHostName();
            //            setting.LocalIp = System.Net.IPAddress.Any;
            //            setting.UseKooboo = true;
            //            setting.Port = 50025;
            //            return setting;
            //#endif

            if (IsOnlineServer)
            {
                setting.UseKooboo = true;
                setting.Port = 50025;
                setting.Server = "mta3.kmailserver.com";
                setting.OkToSend = true;
                setting.HostName = System.Net.Dns.GetHostName();
                setting.LocalIp = System.Net.IPAddress.Any;
            }
            else
            {
                var orgDb = Kooboo.Mail.Factory.DBFactory.OrgDb(mailFrom);
                if (orgDb != null)
                {
                    var smtpSetting = orgDb.SmtpGet();
                    if (smtpSetting != null && !string.IsNullOrWhiteSpace(smtpSetting.Server) && smtpSetting.Port > 0)
                    {
                        // Port must be copied from the configured SMTP setting; otherwise
                        // SendSetting defaults to 25, which cloud providers (OCI, AWS, GCP)
                        // block for outbound traffic.
                        return new SendSetting() { OkToSend = true, CustomSmtp = true, Server = smtpSetting.Server, Port = smtpSetting.Port, UserName = smtpSetting.UserName, Password = smtpSetting.Password, HostName = System.Net.Dns.GetHostName() };
                    }
                }


                var mxs = await Kooboo.Mail.Utility.SmtpUtility.GetMxRecords(RCPTTO);
                if (mxs == null || mxs.Count() == 0)
                {
                    setting.OkToSend = false;
                    setting.ErrorMessage = "Mx records not found";
                }
                else
                {
                    setting.OkToSend = true;
                    setting.Mxs = mxs;

                    setting.LocalIp = System.Net.IPAddress.Any;
                    setting.HostName = System.Net.Dns.GetHostName();
                }
            }

            return setting;
        }

        public static string ImapDomain
        {
            get
            {
                //#if DEBUG
                //                {
                //                    return "mx.localkooboo.com"; 
                //                }
                //#endif
                return "mx.imapsetting.com";
            }
        }

        public static string SmtpDomain
        {
            get
            {
                //#if DEBUG
                //                {
                //                    return "mx.localkooboo.com";
                //                }
                //#endif
                return "mx.sitepapa.com";
            }
        }

        public static string Port587SmtpDomain
        {
            get
            {
                return "mx.imapsetting.com";
            }
        }

        public static bool ForwardRequired
        {
            get; set;

        }

        public static X509Certificate2 LoadCertificateFromFile(string hostName)
        {
            if (string.IsNullOrEmpty(hostName))
            {
                return null;
            }

            // Normalize hostname (idn to ascii) to match directory name if idn is used
            string asciiHost = Kooboo.Lib.Domain.IdnHelper.GetAscii(hostName).ToLower();

            string currentHost = asciiHost;
            while (!string.IsNullOrEmpty(currentHost) && currentHost.Contains("."))
            {
                string certFolder = $"/etc/letsencrypt/live/{currentHost}";
                string certPath = Path.Combine(certFolder, "fullchain.pem");
                string keyPath = Path.Combine(certFolder, "privkey.pem");

                if (File.Exists(certPath) && File.Exists(keyPath))
                {
                    try
                    {
                        // Natively load PEM certificate in modern .NET
                        return X509Certificate2.CreateFromPemFile(certPath, keyPath);
                    }
                    catch (Exception ex)
                    {
                        Kooboo.Data.Log.Instance.Exception.Write($"Failed to load PEM certificate for {currentHost} from {certFolder}: {ex}");
                    }
                }

                // Strip the first subdomain part to fallback to parent domains/wildcards
                int firstDot = currentHost.IndexOf('.');
                if (firstDot >= 0 && firstDot < currentHost.Length - 1)
                {
                    currentHost = currentHost.Substring(firstDot + 1);
                }
                else
                {
                    break;
                }
            }

            return null;
        }

    }
}