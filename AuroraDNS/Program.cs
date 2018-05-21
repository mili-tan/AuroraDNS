using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ARSoft.Tools.Net.Dns;
using MojoUnity;

// ReSharper disable UnusedParameter.Local
#pragma warning disable 1998

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
                        Console.WriteLine(clientAddress + " : " + dnsQuestion.Name);
                        response.ReturnCode = ReturnCode.NoError;
                        string resolvedIp = ResolveOverHttps(clientAddress.ToString(), dnsQuestion.Name.ToString());
                        ARecord aRecord = new ARecord(dnsQuestion.Name, 36000, IPAddress.Parse(resolvedIp));
                        response.AnswerRecords.Add(aRecord);
                    }
                }
            }

            e.Response = response;
        }

        private static string ResolveOverHttps(string ClientIpAddress, string DomainName)
        {
            string dnsStr = new WebClient().DownloadString(
                "https://cloudflare-dns.com/dns-query?ct=application/dns-json" +
                $"&name={DomainName}&type=A&edns_client_subnet={ClientIpAddress}");
            JsonValue dnsAnswerJson = Json.Parse(dnsStr).AsObjectGet("Answer");
            string ipAnswerStr = dnsAnswerJson.AsArrayGet(0).AsObjectGetString("data");
            return IsIp(ipAnswerStr) ? ipAnswerStr : ResolveOverHttps(ClientIpAddress, ipAnswerStr);
        }

        private static bool IsIp(string ip)
        {
            return Regex.IsMatch(ip, @"^((2[0-4]\d|25[0-5]|[01]?\d\d?)\.){3}(2[0-4]\d|25[0-5]|[01]?\d\d?)$");
        }
    }
}
