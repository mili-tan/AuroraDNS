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
                        List<ARecord> resolvedDnsList =
                            ResolveOverHttps(clientAddress.ToString(), dnsQuestion.Name.ToString());

                        response.AnswerRecords.AddRange(resolvedDnsList);

                    }
                }
            }

            e.Response = response;

        }

        private static List<ARecord> ResolveOverHttps(string clientIpAddress, string domainName)
        {
            string dnsStr = new WebClient().DownloadString(
                "https://1.0.0.1/dns-query?ct=application/dns-json" +
                $"&name={domainName}&type=A&edns_client_subnet={clientIpAddress}");
            List<JsonValue> dnsAnswerJsonList = Json.Parse(dnsStr).AsObjectGetArray("Answer");

            List<ARecord> aRecordList = new List<ARecord>();
            foreach (var itemJsonValue in dnsAnswerJsonList)
            {
                string answerAddr = itemJsonValue.AsObjectGetString("data");
                string answerDomainName = itemJsonValue.AsObjectGetString("name");
                int ttl = itemJsonValue.AsObjectGetInt("TTL");

                ARecord aRecord = new ARecord(
                    DomainName.Parse(answerDomainName), ttl, IPAddress.Parse(answerAddr));

                aRecordList.Add(aRecord);
            }

            return !IsIp(aRecordList[0].Address.ToString()) ? ResolveOverHttps(clientIpAddress, aRecordList[0].Address.ToString()) : aRecordList;
        }

        private static bool IsIp(string ip)
        {
            return Regex.IsMatch(ip, @"^((2[0-4]\d|25[0-5]|[01]?\d\d?)\.){3}(2[0-4]\d|25[0-5]|[01]?\d\d?)$");
        }
    }
}
