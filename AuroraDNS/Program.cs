using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private static List<DomainName> BlackList;
        private static Dictionary<DomainName, IPAddress> WhiteList;

        public static class ADnsSetting
        {
            public static string HttpsDnsUrl = "https://1.0.0.1/dns-query";
            //public static string HttpsDnsUrl = "https://dns.google.com/resolve";

            public static IPAddress ListenIp = IPAddress.Any;
            public static IPAddress EDnsIp = IPAddress.Any;
            public static bool EDnsPrivacy;
            public static bool ProxyEnable;
            public static bool DebugLog;
            public static bool BlackListEnable;
            public static bool WhiteListEnable;
            public static WebProxy WProxy = new WebProxy("127.0.0.1:1080");
        }

        static void Main(string[] args)
        {
            LocIPAddr = IPAddress.Parse(GetLocIp());
            MyIPAddr = IPAddress.Parse(new WebClient().DownloadString("https://api.ipify.org"));

            if (!string.IsNullOrWhiteSpace(string.Join("",args)))
                ReadConfig(args[0]);
            if (File.Exists("config.json"))
                ReadConfig("config.json");

            if (ADnsSetting.BlackListEnable)
            {
                string[] blackListStrs = File.ReadAllLines("black.list");
                BlackList = Array.ConvertAll(blackListStrs, DomainName.Parse).ToList();

                if (ADnsSetting.DebugLog)
                {
                    Console.WriteLine("-------Black List-------");
                    foreach (var itemName in BlackList)
                    {
                        Console.WriteLine(itemName.ToString());
                    }
                }
            }

            if (ADnsSetting.WhiteListEnable)
            {
                string[] whiteListStrs = File.ReadAllLines("white.list");
                WhiteList = whiteListStrs.Select(
                    itemStr => itemStr.Split(' ', ',', '\t')).ToDictionary(
                    whiteSplit => DomainName.Parse(whiteSplit[1]),
                    whiteSplit => IPAddress.Parse(whiteSplit[0]));

                if (ADnsSetting.DebugLog)
                {
                    Console.WriteLine("-------White List-------");
                    foreach (var itemName in WhiteList)
                    {
                        Console.WriteLine(itemName.Key + " : " + itemName.Value);
                    }
                }
            }

            using (DnsServer dnsServer = new DnsServer(ADnsSetting.ListenIp, 10, 10))
            {
                dnsServer.QueryReceived += ServerOnQueryReceived;
                dnsServer.Start();
                Console.WriteLine("-------AURORA DNS-------");
                Console.WriteLine("AuroraDNS Server Running");
                Console.WriteLine("Press any key to stop dns server");
                Console.WriteLine("------------------------");
                Console.ReadLine();
                Console.WriteLine("------------------------");
            }
        }

        private static async Task ServerOnQueryReceived(object sender, QueryReceivedEventArgs e)
        {
            if (!(e.Query is DnsMessage query))
                return;

            IPAddress clientAddress = e.RemoteEndpoint.Address;
            if (ADnsSetting.EDnsPrivacy)
                clientAddress = ADnsSetting.EDnsIp;
            else if (Equals(clientAddress, IPAddress.Loopback) || InSameLaNet(clientAddress, LocIPAddr))
                clientAddress = MyIPAddr;

            DnsMessage response = query.CreateResponseInstance();

            if (query.Questions.Count <= 0)
                response.ReturnCode = ReturnCode.ServerFailure;
            
            else
            {
                if (query.Questions[0].RecordType == RecordType.A)
                {
                    foreach (DnsQuestion dnsQuestion in query.Questions)
                    {
                        if (ADnsSetting.DebugLog)
                        {
                            Console.WriteLine(clientAddress + " : " + dnsQuestion.Name);
                        }

                        response.ReturnCode = ReturnCode.NoError;

                        if (ADnsSetting.BlackListEnable && BlackList.Contains(dnsQuestion.Name))
                        {
                            //BlackList
                            ARecord blackRecord = new ARecord(dnsQuestion.Name, 10, IPAddress.Any);
                            response.AnswerRecords.Add(blackRecord);
                            if (ADnsSetting.DebugLog)
                            {
                                Console.WriteLine("|- BlackList");
                            }
                        }

                        else if (ADnsSetting.WhiteListEnable && WhiteList.ContainsKey(dnsQuestion.Name))
                        {
                            //WhiteList
                            ARecord blackRecord = new ARecord(dnsQuestion.Name, 10, WhiteList[dnsQuestion.Name]);
                            response.AnswerRecords.Add(blackRecord);
                            if (ADnsSetting.DebugLog)
                            {
                                Console.WriteLine("|- WhiteList");
                            }
                        }

                        else
                        {
                            //Resolve
                            List<dynamic> resolvedDnsList =
                                ResolveOverHttps(clientAddress.ToString(), dnsQuestion.Name.ToString(),
                                ADnsSetting.ProxyEnable, ADnsSetting.WProxy);
                            foreach (var item in resolvedDnsList)
                            {
                                response.AnswerRecords.Add(item);
                            }
                        }

                    }
                }
            }

            e.Response = response;

        }

        private static List<dynamic> ResolveOverHttps(string clientIpAddress, string domainName,
            bool proxyEnable = false, IWebProxy wProxy = null)
        {
            string dnsStr;
            List<dynamic> recordList = new List<dynamic>();

            using (WebClient webClient = new WebClient())
            {
                if (proxyEnable)
                    webClient.Proxy = wProxy;

                dnsStr = webClient.DownloadString(
                    ADnsSetting.HttpsDnsUrl +
                    @"?ct=application/dns-json&" +
                    $"name={domainName}&type=A&edns_client_subnet={clientIpAddress}");
            }

            List<JsonValue> dnsAnswerJsonList = Json.Parse(dnsStr).AsObjectGetArray("Answer");

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
                        DomainName.Parse(answerDomainName), ttl, DomainName.Parse(answerAddr));

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

        private static bool InSameLaNet(IPAddress ipA, IPAddress ipB)
        {
            return ipA.GetHashCode() % 65536L == ipB.GetHashCode() % 65536L;
        }

        private static string GetLocIp()
        {
            try
            {
                using (TcpClient tcpClient = new TcpClient())
                {
                    tcpClient.Connect("www.sjtu.edu.cn", 80);
                    return ((IPEndPoint) tcpClient.Client.LocalEndPoint).Address.ToString();
                }
            }
            catch (Exception)
            {
                return "192.168.0.1";
            }
        }

        private static void ReadConfig(string path)
        {
            Console.WriteLine("------Read Config-------");

            JsonValue configJson = Json.Parse(File.ReadAllText(path));
            try
            {
                ADnsSetting.ListenIp = IPAddress.Parse(configJson.AsObjectGetString("Listen"));
            }
            catch 
            {
                ADnsSetting.ListenIp = IPAddress.Any;
            }

            try
            {
                ADnsSetting.BlackListEnable = configJson.AsObjectGetBool("BlackList");
            }
            catch
            {
                ADnsSetting.BlackListEnable = false;
            }

            try
            {
                ADnsSetting.WhiteListEnable = configJson.AsObjectGetBool("WhiteList");
            }
            catch
            {
                ADnsSetting.WhiteListEnable = false;
            }

            try
            {
                ADnsSetting.ProxyEnable = configJson.AsObjectGetBool("ProxyEnable");
            }
            catch
            {
                ADnsSetting.ProxyEnable = false;
            }

            try
            {
                ADnsSetting.EDnsPrivacy = configJson.AsObjectGetBool("EDnsPrivacy");
            }
            catch
            {
                ADnsSetting.EDnsPrivacy = false;
            }

            try
            {
                ADnsSetting.DebugLog = configJson.AsObjectGetBool("DebugLog");
            }
            catch
            {
                ADnsSetting.DebugLog = false;
            }

            try
            {
                ADnsSetting.EDnsIp = IPAddress.Parse(configJson.AsObjectGetString("EDnsClient"));
            }
            catch
            {
                ADnsSetting.EDnsIp = IPAddress.Any;
            }

            try
            {
                ADnsSetting.HttpsDnsUrl = configJson.AsObjectGetString("HttpsDns");
                if (string.IsNullOrEmpty(ADnsSetting.HttpsDnsUrl))
                {
                    ADnsSetting.HttpsDnsUrl = "https://1.0.0.1/dns-query";
                }
            }
            catch
            {
                ADnsSetting.HttpsDnsUrl = "https://1.0.0.1/dns-query";
            }

            Console.WriteLine("Listen      : " + ADnsSetting.ListenIp);
            Console.WriteLine("BlackList   : " + ADnsSetting.BlackListEnable);
            Console.WriteLine("ProxyEnable : " + ADnsSetting.ProxyEnable);
            Console.WriteLine("DebugLog    : " + ADnsSetting.DebugLog);
            Console.WriteLine("EDnsPrivacy : " + ADnsSetting.EDnsPrivacy);
            Console.WriteLine("EDnsClient  : " + ADnsSetting.EDnsIp);
            Console.WriteLine("HttpsDns    : " + ADnsSetting.HttpsDnsUrl);

            if (ADnsSetting.ProxyEnable)
            {
                ADnsSetting.WProxy = new WebProxy(configJson.AsObjectGetString("Proxy"));
                Console.WriteLine("ProxyServer : " + configJson.AsObjectGetString("Proxy"));
            }
        }

    }
}
