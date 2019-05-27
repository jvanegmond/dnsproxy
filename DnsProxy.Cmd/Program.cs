using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DnsProxy.Cmd
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var service = new DnsProxyService())
            {
                Console.WriteLine("Press ENTER to gracefully shut down the service");
                Console.ReadLine();
            }
        }
    }
}
