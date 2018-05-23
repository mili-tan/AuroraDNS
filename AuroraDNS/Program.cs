using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
                        List<ADns> resolvedDnsList = ResolveOverHttps(clientAddress.ToString(), dnsQuestion.Name.ToString());
                        foreach (var dnsItem in resolvedDnsList)
                        {
                            ARecord aRecord = new ARecord(dnsQuestion.Name, dnsItem.ttl,
                                IPAddress.Parse(dnsItem.answerAddr));
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
                ADns aDns = new ADns();
                aDns.answerAddr = itemJsonValue.AsObjectGetString("data");
                aDns.domainName = itemJsonValue.AsObjectGetString("name");
                aDns.ttl = itemJsonValue.AsObjectGetInt("TTL");
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
