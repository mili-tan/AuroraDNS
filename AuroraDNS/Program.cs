using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using ARSoft.Tools.Net;
using ARSoft.Tools.Net.Dns;

namespace AuroraDNS
{
    class Program
    {
        static void Main(string[] args)
        {
            using (DnsServer server = new DnsServer(IPAddress.Any, 10, 10))
            {
                server.QueryReceived += ServerOnQueryReceived;
                server.Start();
                Console.WriteLine("press any key to stop dns server");
                Console.ReadLine();
            }
        }

        private static async Task ServerOnQueryReceived(object sender, QueryReceivedEventArgs e)
        {
            IPAddress clientAddress = e.RemoteEndpoint.Address;
            DnsMessage query = e.Query as DnsMessage;

            if (query == null)
                return;

            DnsMessage response = query.CreateResponseInstance();

            if (query.Questions.Count <= 0)
            {
                response.ReturnCode = ReturnCode.ServerFailure;
            }
            else
            {
                if (query.Questions[0].RecordType == RecordType.A)
                {
                    foreach (DnsQuestion dnsQuestion in query.Questions)
                    {
                        Console.WriteLine(clientAddress + " : " +dnsQuestion.Name);
                        response.ReturnCode = ReturnCode.NoError;
                        //string resolvedIp = ResolveOverHttps(clientAddress.ToString(), dnsQuestion.Name);
                        ARecord aRecord = new ARecord(dnsQuestion.Name, 36000, IPAddress.Parse("1.1.1.1"));
                        response.AnswerRecords.Add(aRecord);
                    }
                }
            }

            e.Response = response;
        }

        private string ResolveOverHttps(string ClientIpAddress, string DomainName)
        {
            return "";
        }
    }
}
