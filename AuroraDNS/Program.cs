using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
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
        private static IPAddress IntIPAddr;
        private static IPAddress LocIPAddr;
        private static ConsoleColor OriginColor;
        private static List<DomainName> BlackList;
        private static Dictionary<DomainName, IPAddress> WhiteList;

        public static class ADnsSetting
        {

            public static string HttpsDnsUrl = "https://1.0.0.1/dns-query";
            //"https://dns.google.com/resolve";
            //"https://dnsp.mili.one:23233/dns-query";
            //"https://dnsp1.mili.one:23233/dns-query";
            //"https://plus1s.site/extdomains/dns.google.com/resolve";

            public static IPAddress ListenIp = IPAddress.Any;
            public static IPAddress EDnsIp = IPAddress.Any;
            public static bool EDnsCustomize;
            public static bool ProxyEnable;
            public static bool DebugLog;
            public static bool BlackListEnable;
            public static bool WhiteListEnable;
            public static WebProxy WProxy = new WebProxy("127.0.0.1:1080");
        }

        static void Main(string[] args)
        {
            Console.WriteLine(Resource.ASCII);

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            OriginColor = Console.ForegroundColor;
            LocIPAddr = IPAddress.Parse(GetLocIp());
            if (Thread.CurrentThread.CurrentCulture.Name == "zh-CN")
            {
                IntIPAddr = IPAddress.Parse(new WebClient().DownloadString("http://members.3322.org/dyndns/getip").Trim());
            }
            else
            {
                IntIPAddr = IPAddress.Parse(new WebClient().DownloadString("https://api.ipify.org").Trim());
            }


            Console.Clear();

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
                    Console.WriteLine(@"-------Black List-------");
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
                    Console.WriteLine(@"-------White List-------");
                    foreach (var itemName in WhiteList)
                    {
                        Console.WriteLine(itemName.Key + @" : " + itemName.Value);
                    }
                }
            }

            using (DnsServer dnsServer = new DnsServer(ADnsSetting.ListenIp, 10, 10))
            {
                dnsServer.QueryReceived += ServerOnQueryReceived;
                dnsServer.Start();
                Console.WriteLine(@"-------AURORA DNS-------");

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(DateTime.Now);
                Console.WriteLine(@"AuroraDNS Server Running");
                Console.ForegroundColor = OriginColor;

                Console.WriteLine(@"Press any key to stop dns server");
                Console.WriteLine(Resource.Line);
                Console.ReadLine();
                Console.WriteLine(Resource.Line);
            }
        }

        private static async Task ServerOnQueryReceived(object sender, QueryReceivedEventArgs e)
        {
            if (!(e.Query is DnsMessage query))
                return;

            IPAddress clientAddress = e.RemoteEndpoint.Address;
            if (ADnsSetting.EDnsCustomize)
                clientAddress = ADnsSetting.EDnsIp;
            else if (Equals(clientAddress, IPAddress.Loopback))
                clientAddress = IntIPAddr;
            else if (InSameLaNet(clientAddress, LocIPAddr) && !Equals(IntIPAddr, LocIPAddr))
                clientAddress = IntIPAddr;

            DnsMessage response = query.CreateResponseInstance();

            if (query.Questions.Count <= 0)
                response.ReturnCode = ReturnCode.ServerFailure;
            
            else
            {
                if (query.Questions[0].RecordType == RecordType.A)
                {
                    foreach (DnsQuestion dnsQuestion in query.Questions)
                    {
                        response.ReturnCode = ReturnCode.NoError;

                        if (ADnsSetting.DebugLog)
                        {
                            Console.WriteLine($@"| {DateTime.Now} {clientAddress} : { dnsQuestion.Name}");
                        }

                        if (ADnsSetting.BlackListEnable && BlackList.Contains(dnsQuestion.Name))
                        {
                            if (ADnsSetting.DebugLog)
                            {
                                Console.WriteLine(@"|- BlackList");
                            }
                            
                            //BlackList
                            response.ReturnCode = ReturnCode.NxDomain;
                            //response.AnswerRecords.Add(new ARecord(dnsQuestion.Name, 10, IPAddress.Any));
                        }

                        else if (ADnsSetting.WhiteListEnable && WhiteList.ContainsKey(dnsQuestion.Name))
                        {
                            if (ADnsSetting.DebugLog)
                            {
                                Console.WriteLine(@"|- WhiteList");
                            }
                            
                            //WhiteList
                            ARecord blackRecord = new ARecord(dnsQuestion.Name, 10, WhiteList[dnsQuestion.Name]);
                            response.AnswerRecords.Add(blackRecord);
                        }

                        else
                        {
                            //Resolve
                            try
                            {
                                var (resolvedDnsList, statusCode) = ResolveOverHttps(clientAddress.ToString(), dnsQuestion.Name.ToString(),
                                    ADnsSetting.ProxyEnable, ADnsSetting.WProxy);
                                if (resolvedDnsList != null)
                                {
                                    foreach (var item in resolvedDnsList)
                                    {
                                        response.AnswerRecords.Add(item);
                                    }
                                }
                                else
                                {
                                    response.ReturnCode = (ReturnCode)statusCode;
                                }
                            }
                            catch (Exception ex)
                            {
                                response.ReturnCode = ReturnCode.ServerFailure;
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine(@"| " + ex);
                                Console.ForegroundColor = OriginColor;
                            }
                        }

                    }
                }
            }

            e.Response = response;

        }

        private static (List<dynamic> list,int statusCode) ResolveOverHttps(string clientIpAddress, string domainName,
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

            JsonValue dnsJsonValue = Json.Parse(dnsStr);
            int statusCode = dnsJsonValue.AsObjectGetInt("Status");
            if (statusCode != 0)
            {
                return (null,statusCode);
            }

            List<JsonValue> dnsAnswerJsonList = dnsJsonValue.AsObjectGetArray("Answer");

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

            return (recordList,statusCode);
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
            Console.WriteLine(@"------Read Config-------");

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
                ADnsSetting.EDnsCustomize = configJson.AsObjectGetBool("EDnsPrivacy");
            }
            catch
            {
                ADnsSetting.EDnsCustomize = false;
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
                ADnsSetting.EDnsIp = IPAddress.Parse(configJson.AsObjectGetString("EDnsClientIp"));
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

            Console.WriteLine(@"Listen      : " + ADnsSetting.ListenIp);
            Console.WriteLine(@"BlackList   : " + ADnsSetting.BlackListEnable);
            Console.WriteLine(@"WhiteList   : " + ADnsSetting.WhiteListEnable);
            Console.WriteLine(@"ProxyEnable : " + ADnsSetting.ProxyEnable);
            Console.WriteLine(@"DebugLog    : " + ADnsSetting.DebugLog);
            Console.WriteLine(@"EDnsClient  : " + ADnsSetting.EDnsIp);
            Console.WriteLine(@"HttpsDns    : " + ADnsSetting.HttpsDnsUrl);
            Console.WriteLine(@"EDnsCustomize : " + ADnsSetting.EDnsCustomize);

            if (ADnsSetting.ProxyEnable)
            {
                ADnsSetting.WProxy = new WebProxy(configJson.AsObjectGetString("Proxy"));
                Console.WriteLine(@"ProxyServer : " + configJson.AsObjectGetString("Proxy"));
            }
        }

    }
}
