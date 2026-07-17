//Copyright (c) 2018 Yardi Technology Limited. Http://www.kooboo.com 
//All rights reserved.
using System.Linq;
using Kooboo.Api;
using Kooboo.Data;
using Kooboo.Data.Config;
using Kooboo.Data.Models;
using Kooboo.Data.Permission;
using Kooboo.Data.SSL;
using Kooboo.Web.ViewModel;

namespace Kooboo.Web.Api.Implementation
{
    public class BindingApi : IApi
    {
        public string ModelName
        {
            get
            {
                return "binding";
            }
        }

        public bool RequireSite
        {
            get
            {
                return false;
            }
        }

        public bool RequireUser
        {
            get
            {
                return true;
            }
        }

        [Kooboo.Attributes.RequireParameters("id")]
        [Permission(Feature.DOMAIN, Action = Data.Permission.Action.DELETE)]
        public void Delete(ApiCall call)
        {
            Data.Config.AppHost.BindingRepo.Delete(call.ObjectId);
        }

        [Permission(Feature.DOMAIN, Action = Data.Permission.Action.DELETE)]
        public virtual bool Deletes(ApiCall call)
        {

            string json = call.GetValue("ids");
            if (string.IsNullOrEmpty(json))
            {
                json = call.Context.Request.Body;
            }

            List<Guid> ids = new List<Guid>();

            try
            {
                ids = Lib.Helper.JsonHelper.Deserialize<List<Guid>>(json);
            }
            catch (Exception)
            {
                //throw;
            }

            if (ids != null && ids.Count() > 0)
            {
                foreach (var item in ids)
                {
                    Data.Config.AppHost.BindingRepo.Delete(item);
                }
                return true;
            }
            return false;
        }

        [Kooboo.Attributes.RequireParameters("SiteId")]
        [Permission(Feature.DOMAIN, Action = Data.Permission.Action.EDIT)]
        public virtual void Post(ApiCall call)
        {
            string subdomain = Kooboo.Lib.Domain.IdnHelper.GetAscii(call.GetValue("subdomain"));
            string RootDomain = Kooboo.Lib.Domain.IdnHelper.GetAscii(call.GetValue("rootdomain"));
            string redirect = call.GetValue("redirect");
            string culture = call.GetValue("culture");
            Guid SiteId = call.GetGuidValue("SiteId");
            var sitename = Data.Config.AppHost.SiteRepo.Get(SiteId)?.Name;
            int port = (int)call.GetLongValue("Port");

            if (string.IsNullOrEmpty(RootDomain))
            {
                return;
            }

            bool DefaultBinding = call.GetBoolValue("DefaultBinding");

            if (SiteId != default(Guid))
            {

                if (port <= 0)
                {
                    DefaultBinding = false;
                }


                if (!DefaultBinding)
                {
                    port = 0;
                }


                if (port > 0)
                {
                    if (SystemStart.WebServer.Ports.Contains(port) || Lib.Helper.NetworkHelper.IsPortInUse(port))
                    {
                        throw new Exception(Data.Language.Hardcoded.GetValue("Port", call.Context) + " " + port.ToString() + " " + Data.Language.Hardcoded.GetValue("is occupied", call.Context));
                    }
                }


                if (DefaultBinding && port > 0)
                {
                    SiteBinding bind = new SiteBinding();
                    bind.OrganizationId = call.Context.User.CurrentOrgId;
                    bind.WebSiteId = SiteId;
                    // bind.WebSiteName = sitename;
                    bind.Port = port;
                    bind.FullDomain = Data.Config.ConfigHelper.ToFullDomain(RootDomain, subdomain);
                    bind.Redirect = redirect;
                    bind.Culture = culture;
                    Data.Config.AppHost.BindingRepo.AddOrUpdate(bind);
                }
                else
                {

                    var domain = GlobalDb.Domains.Get(RootDomain);
                    if (domain.OrganizationId != default(Guid) && AppSettings.IsOnlineServer && domain.OrganizationId != call.Context.User.CurrentOrgId)
                    {
                        throw new Exception(Data.Language.Hardcoded.GetValue("Domain does not owned by current user", call.Context));
                    }

                    if (domain.MailOnly)
                    {
                        throw new Exception(Data.Language.Hardcoded.GetValue("Domain configure to use mail server only", call.Context));
                    }

                    // TODO: if domain is share domain. 

                    SiteBinding bind = new SiteBinding();
                    bind.FullDomain = Data.Config.ConfigHelper.ToFullDomain(RootDomain, subdomain);
                    bind.WebSiteId = SiteId;
                    // bind.WebSiteName = sitename;
                    bind.OrganizationId = call.Context.User.CurrentOrgId;
                    bind.Port = port;
                    bind.Redirect = redirect;
                    bind.Culture = culture;

                    Data.Config.AppHost.BindingRepo.AddOrUpdate(bind);
                }

            }
        }


        [Permission(Feature.DOMAIN, Action = Data.Permission.Action.VIEW)]
        public List<BindingViewModel> ListBySite(Guid SiteId, ApiCall call)
        {
            List<BindingViewModel> result = new List<BindingViewModel>();

            var list = Data.Config.AppHost.BindingService.GetBySiteId(SiteId);

            foreach (var item in list)
            {
                BindingViewModel model = new BindingViewModel(item);
                // SSL certificates are stored under the Punycode name; the view model
                // FullName is converted to Unicode for display, so convert back for lookup.
                var punycodeName = Kooboo.Lib.Domain.IdnHelper.GetAscii(model.FullName);
                model.EnableSsl = HasSsl(punycodeName);
                model.SslError = SslService.GetError(punycodeName);
                result.Add(model);
            }

            return result;
        }

        [Kooboo.Attributes.RequireParameters("domainid")]
        public List<BindingInfo> ListByDomain(ApiCall call)
        {
            Guid domainid = call.GetGuidValue("domainid");
            if (domainid != default(Guid))
            {
                var domain = GlobalDb.Domains.Get(domainid, call.Context.User.CurrentOrgId);
                if (domain == null)
                {
                    return new List<BindingInfo>();
                }

                var user = call.Context.User;

                var bindings = Data.Config.AppHost.BindingService.GetByRootDomain(domain.DomainName, false);
                List<BindingInfo> bindinginfos = new List<BindingInfo>();
                foreach (var item in bindings)
                {

                    if (!Kooboo.Sites.Service.WebSiteService.UserHasRight(user, item.WebSiteId))
                    {
                        continue;
                    }

                    BindingInfo info = new BindingInfo();
                    info.Id = item.Id;
                    info.FullName = Kooboo.Mail.Utility.AddressUtility.GetUnicodeDomain(item.FullDomain);
                    info.SubDomain = Kooboo.Mail.Utility.AddressUtility.GetUnicodeDomain(item.GetSubDomain());
                    info.OrganizationId = item.OrganizationId;

                    if (item.Port > 0 && item.Port != 80)
                    {
                        info.Port = item.Port;
                    }

                    var site = Data.Config.AppHost.SiteRepo.Get(item.WebSiteId);
                    info.SiteName = site != null ? site.Name : "";
                    info.Device = item.Device;
                    info.DomainId = item.DomainId;
                    info.WebSiteId = item.WebSiteId;

                    var punycodeName = Kooboo.Lib.Domain.IdnHelper.GetAscii(info.FullName);
                    info.EnableSsl = HasSsl(punycodeName);
                    info.SslError = SslService.GetError(punycodeName);

                    bindinginfos.Add(info);
                }
                return bindinginfos;
            }

            return null;
        }


        public bool VerifySsl(string rootDomain, ApiCall call)
        {
            // Bypassed DNS/IP pre-flight check to allow SSL generation for domains proxied by Cloudflare.
            return true;
        }

        public void SetSsl(string rootDomain, ApiCall call)
        {
            string Subdomain = Kooboo.Lib.Domain.IdnHelper.GetAscii(call.GetValue("Subdomain"));
            rootDomain = Kooboo.Lib.Domain.IdnHelper.GetAscii(rootDomain);

            string fullName = ConfigHelper.ToFullDomain(rootDomain, Subdomain);

            Guid OrgId = default(Guid);

            if (call.Context.WebSite != null)
            {
                OrgId = call.Context.WebSite.OrganizationId;
            }
            else
            {
                if (call.Context.User != null)
                {
                    OrgId = call.Context.User.CurrentOrgId;
                }
            }
            Data.SSL.SslService.SetSsl(fullName, OrgId, true);
        }

        public List<SiteBindingViewModel> SiteBinding(ApiCall call)
        {
            var user = call.Context.User;
            if (user.CurrentOrgId == default(Guid))
            {
                return null;
            }

            var sites = Data.Config.AppHost.SiteService.ListByOrg(user.CurrentOrgId);

            List<SiteBindingViewModel> result = new List<SiteBindingViewModel>();

            foreach (var item in sites)
            {
                var bindings = Data.Config.AppHost.BindingService.GetBySiteId(item.Id);
                int count = 0;
                if (bindings != null)
                {
                    count = bindings.Count();
                }

                result.Add(new SiteBindingViewModel() { Name = item.Name, Id = item.Id, BindingCount = count });

            }

            return result;

        }


        private bool HasSsl(string fullDomain)
        {
            var item = Kooboo.Data.GlobalDb.SslCertificate.GetByDomain(fullDomain);
            return (item != null);
        }

        public bool IsUniqueName(string name, ApiCall call)
        {
            // Bindings are stored in Punycode; normalize before checking uniqueness.
            name = Kooboo.Lib.Domain.IdnHelper.GetAscii(name);

            var commonshare = new Kooboo.Data.Service.ShareStagingDomainService().IsAvailable(name);

            if (!commonshare)
            {
                return false;
            }

            return !AppHost.BindingRepo.All.Any(a => name.Equals(a.FullDomain, StringComparison.CurrentCultureIgnoreCase));
        }

        public class BindingViewModel
        {
            public BindingViewModel(Binding binding)
            {
                this.Id = binding.Id;
                this.OrganizationId = binding.OrganizationId;
                this.WebSiteId = binding.WebSiteId;
                this.SubDomain = Kooboo.Mail.Utility.AddressUtility.GetUnicodeDomain(binding.SubDomain);
                this.IpAddress = binding.IpAddress;
                this.DefaultPortBinding = binding.DefaultPortBinding;
                this.Device = binding.Device;
                this.DomainId = binding.DomainId;
                this.FullName = Kooboo.Mail.Utility.AddressUtility.GetUnicodeDomain(binding.FullName);
                this.Port = binding.Port;
            }

            public BindingViewModel(SiteBinding binding)
            {
                this.Id = binding.Id;
                this.OrganizationId = binding.OrganizationId;
                this.WebSiteId = binding.WebSiteId;
                this.SubDomain = Kooboo.Mail.Utility.AddressUtility.GetUnicodeDomain(binding.GetSubDomain());
                this.IpAddress = binding.IpAddress;
                this.DefaultPortBinding = binding.IsDefaultPortBinding;
                this.Device = binding.Device;
                // this.DomainId = binding.DomainId;
                this.FullName = Kooboo.Mail.Utility.AddressUtility.GetUnicodeDomain(binding.FullDomain);
                this.Port = binding.Port;
                this.Redirect = binding.Redirect;
                this.Culture = binding.Culture;
                var rootdomain = binding.GetRootDomain();
                this.DomainId = Kooboo.Data.IDGenerator.GetDomainId(rootdomain);
            }

            public bool EnableSsl { get; set; }

            public Guid Id
            {
                get; set;
            }

            public Guid OrganizationId { get; set; }


            public Guid WebSiteId;

            public string SubDomain { get; set; }

            public Guid DomainId { get; set; }

            public string FullName
            {
                get; set;
            }

            public string Device { get; set; }
            public string Redirect { get; set; }
            public string Culture { get; set; }


            public string IpAddress
            {
                get; set;
            }

            // for default port binding..
            public int Port { get; set; } = 0;

            public bool DefaultPortBinding { get; set; }

            public string SslError { get; set; }
        }
    }
}
