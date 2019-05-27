using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace DnsProxy.Service
{
    public partial class Service1 : ServiceBase
    {
        private DnsProxyService _service;

        public Service1()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            _service = new DnsProxyService();
        }

        protected override void OnStop()
        {
            _service.Dispose();
        }
    }
}
