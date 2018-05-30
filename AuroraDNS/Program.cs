using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ARSoft.Tools.Net;
using ARSoft.Tools.Net.Dns;
using MojoUnity;

// ReSharper disable UnusedParameter.Local
#pragma warning disable 1998
#pragma warning disable 169

namespace AuroraDNS
{

    static class Program
    {
        private static IPAddress MyIPAddr;
        private static IPAddress LocIPAddr;

        public static class DnsSetting
        {
            public static string HttpsDnsUrl = "https://1.0.0.1/dns-query";
            //public static string HttpsDnsUrl = "https://dns.google.com/resolve";
            public static IPAddress ListenIp = IPAddress.Any;
            public static IPAddress EDnsIp = IPAddress.Any;
            public static bool EDnsPrivacy = false;
            public static bool ProxyEnable = false;
            public static WebProxy WProxy = new WebProxy("127.0.0.1:10800");
        }

        static void Main(string[] args)
        {
            LocIPAddr = IPAddress.Parse(GetLocIp());
            MyIPAddr = IPAddress.Parse(new WebClient().DownloadString("https://api.ip.la/"));

            //Console.WriteLine(LocIPAddr.GetHashCode() % (long)(256 * 256));

            using (DnsServer dnsServer = new DnsServer(DnsSetting.ListenIp, 10, 10))
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

            if (DnsSetting.EDnsPrivacy)
            {
                clientAddress = DnsSetting.EDnsIp;
            }
            else if (Equals(clientAddress, IPAddress.Loopback) || InSameLANet(clientAddress,LocIPAddr))
            {
                clientAddress = MyIPAddr;
            }


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
                        List<dynamic> resolvedDnsList =
                            ResolveOverHttps(clientAddress.ToString(), dnsQuestion.Name.ToString());

                        foreach (var item in resolvedDnsList)
                        {
                            response.AnswerRecords.Add(item);
                        }
                    }
                }
            }

            e.Response = response;

        }

        private static List<dynamic> ResolveOverHttps(string clientIpAddress, string domainName)
        {
            string dnsStr;
            using (WebClient webClient = new WebClient())
            {
                if (DnsSetting.ProxyEnable)
                {
                    webClient.Proxy = DnsSetting.WProxy;
                }

                dnsStr = webClient.DownloadString(
                    DnsSetting.HttpsDnsUrl +
                    @"?ct=application/dns-json&" +
                    $"name={domainName}&type=A&edns_client_subnet={clientIpAddress}");
            }

            List<JsonValue> dnsAnswerJsonList = Json.Parse(dnsStr).AsObjectGetArray("Answer");

            List<dynamic> recordList = new List<dynamic>();
            foreach (var itemJsonValue in dnsAnswerJsonList)
            {
                string answerAddr = itemJsonValue.AsObjectGetString("data");
                string answerDomainName = itemJsonValue.AsObjectGetString("name");
                int ttl = itemJsonValue.AsObjectGetInt("TTL");
                
                if (IsIp(answerAddr))
                {
                    ARecord aRecord = new ARecord(
                        DomainName.Parse(answerDomainName), ttl, IPAddress.Parse(answerAddr));

                    recordList.Add(aRecord);
                }
                else
                {
                    CNameRecord cRecord = new CNameRecord(
                        DomainName.Parse(answerDomainName),ttl,DomainName.Parse(answerAddr));

                    recordList.Add(cRecord);

                    //recordList.AddRange(ResolveOverHttps(clientIpAddress,answerAddr));
                    //return recordList;
                }
            }

            return recordList;
        }

        private static bool IsIp(string ip)
        {
            return Regex.IsMatch(ip, @"^((2[0-4]\d|25[0-5]|[01]?\d\d?)\.){3}(2[0-4]\d|25[0-5]|[01]?\d\d?)$");
        }

        private static bool InSameLANet(IPAddress ipA, IPAddress ipB)
        {
            if (ipA.GetHashCode() % 65536L == ipB.GetHashCode() % 65536L)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private static string GetLocIp()
        {
            try
            {
                using (TcpClient tcpClient = new TcpClient())
                {
                    tcpClient.Connect("www.sjtu.edu.cn", 80);
                    return ((IPEndPoint)tcpClient.Client.LocalEndPoint).Address.ToString();
                }
            }
            catch (Exception)
            {
                return "192.168.0.1";
            }
        }

    }
}
