using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ARSoft.Tools.Net;
using ARSoft.Tools.Net.Dns;
using MojoUnity;

// ReSharper disable UnusedParameter.Local
#pragma warning disable 1998

namespace AuroraDNS
{
    public class ADns
    {
        public string domainName;
        public string answerAddr;
        public int ttl;
    }

    static class Program
    {
        static void Main(string[] args)
        {
            using (DnsServer dnsServer = new DnsServer(IPAddress.Any, 10, 10))
            {
                dnsServer.QueryReceived += ServerOnQueryReceived;
                dnsServer.Start();
                Console.WriteLine("AuroraDNS Server Running");
                Console.WriteLine("Press any key to stop dns server");
                Console.ReadLine();
            }
        }

        private static async Task ServerOnQueryReceived(object sender, QueryReceivedEventArgs e)
        {
            IPAddress clientAddress = e.RemoteEndpoint.Address;

            if (!(e.Query is DnsMessage query))
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
                        List<ADns> resolvedDnsList = ResolveOverHttps(clientAddress.ToString(), dnsQuestion.Name.ToString());
                        foreach (var dnsItem in resolvedDnsList)
                        {
                            ARecord aRecord = new ARecord(
                                DomainName.Parse(dnsItem.domainName), 
                                dnsItem.ttl,IPAddress.Parse(dnsItem.answerAddr));
                            response.AnswerRecords.Add(aRecord);
                        }
                    }
                }
            }

            e.Response = response;

        }

        private static List<ADns> ResolveOverHttps(string ClientIpAddress, string DomainName)
        {
            string dnsStr = new WebClient().DownloadString(
                "https://1.0.0.1/dns-query?ct=application/dns-json" +
                $"&name={DomainName}&type=A&edns_client_subnet={ClientIpAddress}");
            List<JsonValue> dnsAnswerJsonList = Json.Parse(dnsStr).AsObjectGetArray("Answer");

            List<ADns> aDnsList = new List<ADns>();
            foreach (var itemJsonValue in dnsAnswerJsonList)
            {
                ADns aDns = new ADns
                {
                    answerAddr = itemJsonValue.AsObjectGetString("data"),
                    domainName = itemJsonValue.AsObjectGetString("name"),
                    ttl = itemJsonValue.AsObjectGetInt("TTL")
                };
                aDnsList.Add(aDns);
            }

            return !IsIp(aDnsList[0].answerAddr) ? ResolveOverHttps(ClientIpAddress, aDnsList[0].answerAddr) : aDnsList;
        }

        private static bool IsIp(string ip)
        {
            return Regex.IsMatch(ip, @"^((2[0-4]\d|25[0-5]|[01]?\d\d?)\.){3}(2[0-4]\d|25[0-5]|[01]?\d\d?)$");
        }
    }
}
