using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using ARSoft.Tools.Net;
using ARSoft.Tools.Net.Dns;
using MojoUnity;
using static System.Net.ServicePointManager;

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
        private static List<DomainName> ChinaList;
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
            public static bool IPv6Enable = true;
            public static float TlsVersion = 0;
            public static bool DebugLog = true;
            public static bool BlackListEnable;
            public static bool ChinaListEnable = true;
            public static bool WhiteListEnable;
            public static bool DnsCacheEnable = true;
            public static bool AllowSelfSignedCert;
            public static WebProxy WProxy = new WebProxy("127.0.0.1:1080");
        }

        static void Main(string[] args)
        {
            if (File.Exists("config.json"))
                ReadConfig("config.json");

            Thread.Sleep(1500);
            Console.Clear();
            Console.WriteLine(Resource.ASCII);

            OriginColor = Console.ForegroundColor;
            LocIPAddr = IPAddress.Parse(GetLocIp());

            switch (ADnsSetting.TlsVersion)
            {
                case 1:
                    SecurityProtocol = SecurityProtocolType.Tls;
                    break;
                case 1.1F:
                    SecurityProtocol = SecurityProtocolType.Tls11;
                    break;
                case 1.2F:
                    SecurityProtocol = SecurityProtocolType.Tls12;
                    break;
                default:
                    SecurityProtocol = SecurityProtocolType.Tls12;
                    break;
            }

            if (ADnsSetting.AllowSelfSignedCert)
                ServerCertificateValidationCallback +=
                    (sender, cert, chain, sslPolicyErrors) => true;

            if (Thread.CurrentThread.CurrentCulture.Name == "zh-CN")
                IntIPAddr = IPAddress.Parse(new WebClient().DownloadString("http://members.3322.org/dyndns/getip").Trim());
            else
                IntIPAddr = IPAddress.Parse(new WebClient().DownloadString("https://api.ipify.org").Trim());

            //Console.Clear();

            if (ADnsSetting.BlackListEnable)
            {
                string[] blackListStrs = File.ReadAllLines("black.list");
                BlackList = Array.ConvertAll(blackListStrs, DomainName.Parse).ToList();

                if (ADnsSetting.DebugLog)
                {
                    Console.WriteLine(@"-------Black List-------");
                    foreach (var itemName in BlackList)
                        Console.WriteLine(itemName.ToString());
                }
            }

            if (ADnsSetting.ChinaListEnable)
            {
                string[] chinaListStrs = File.ReadAllLines("china.list");
                ChinaList = Array.ConvertAll(chinaListStrs, DomainName.Parse).ToList();

                if (ADnsSetting.DebugLog)
                {
                    Console.WriteLine(@"-------China List-------");
                    foreach (var itemName in ChinaList)
                        Console.WriteLine(itemName.ToString());
                }
            }

            if (ADnsSetting.WhiteListEnable)
            {
                string[] whiteListStrs;
                if (File.Exists("white.list"))
                    whiteListStrs = File.ReadAllLines("white.list");
                else
                    whiteListStrs = File.ReadAllLines("rewrite.list");

                WhiteList = whiteListStrs.Select(
                    itemStr => itemStr.Split(' ', ',', '\t')).ToDictionary(
                    whiteSplit => DomainName.Parse(whiteSplit[1]),
                    whiteSplit => IPAddress.Parse(whiteSplit[0]));

                if (ADnsSetting.DebugLog)
                {
                    Console.WriteLine(@"-------White List-------");
                    foreach (var itemName in WhiteList)
                        Console.WriteLine(itemName.Key + @" : " + itemName.Value);
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
                Console.ReadKey();
            }
        }

        private static async Task ServerOnQueryReceived(object sender, QueryReceivedEventArgs e)
        {
            if (!(e.Query is DnsMessage query))
                return;

            IPAddress clientAddress = e.RemoteEndpoint.Address;
            if (ADnsSetting.EDnsCustomize)
                if (Equals(ADnsSetting.EDnsIp, IPAddress.Parse("0.0.0.1")))
                    clientAddress = IPAddress.Parse(IntIPAddr.ToString().Substring(0,
                                                        IntIPAddr.ToString()
                                                            .LastIndexOf(".", StringComparison.Ordinal)) + ".0");
                else
                    clientAddress = ADnsSetting.EDnsIp;

            else if (Equals(clientAddress, IPAddress.Loopback))
                clientAddress = IntIPAddr;
            else if (InSameLaNet(clientAddress, LocIPAddr) && !Equals(IntIPAddr, LocIPAddr))
                clientAddress = IntIPAddr;
            DnsMessage response = query.CreateResponseInstance();

            try
            {
                if (query.Questions.Count <= 0)
                    response.ReturnCode = ReturnCode.ServerFailure;
                else
                {
                    foreach (DnsQuestion dnsQuestion in query.Questions)
                    {
                        response.ReturnCode = ReturnCode.NoError;
                        if (ADnsSetting.DebugLog)
                            Console.WriteLine(
                                $@"| {DateTime.Now} {clientAddress} : {dnsQuestion.Name} | {dnsQuestion.RecordType.ToString().ToUpper()}");

                        if (ADnsSetting.DnsCacheEnable && MemoryCache.Default.Contains($"{dnsQuestion.Name}{dnsQuestion.RecordType}"))
                        {
                            response.AnswerRecords.AddRange(
                                (List<DnsRecordBase>)MemoryCache.Default.Get($"{dnsQuestion.Name}{dnsQuestion.RecordType}"));
                            response.AnswerRecords.Add(new TxtRecord(DomainName.Parse("cache.auroradns.mili.one"), 0,
                                "AuroraDNSC Cached"));

                            if (ADnsSetting.DebugLog)
                                Console.WriteLine
                                    ($@"|- CacheContains : {dnsQuestion.Name} | Count : {MemoryCache.Default.Count()}");
                        }

                        if (ADnsSetting.BlackListEnable && BlackList.Contains(dnsQuestion.Name) &&
                            dnsQuestion.RecordType == RecordType.A)
                        {
                            if (ADnsSetting.DebugLog)
                                Console.WriteLine(@"|- BlackList");

                            //BlackList
                            response.ReturnCode = ReturnCode.NxDomain;
                        }

                        if (ADnsSetting.ChinaListEnable && dnsQuestion.RecordType == RecordType.A)
                        {
                            var domainSplit = dnsQuestion.Name.ToString().TrimEnd('.').Split('.');
                            var nameStr = $"{domainSplit[domainSplit.Length - 2]}.{domainSplit[domainSplit.Length - 1]}";
                            if (ChinaList.Contains(DomainName.Parse(nameStr)) || dnsQuestion.Name.ToString().Contains(".cn") ||
                                dnsQuestion.Name.ToString().Contains(".xn--"))
                            {
                                var resolvedDnsList = ResolveOverDNSPod(dnsQuestion.Name.ToString());
                                if (resolvedDnsList != null && resolvedDnsList != new List<dynamic>())
                                    foreach (var item in resolvedDnsList)
                                        response.AnswerRecords.Add(item);
                                else
                                    response.ReturnCode = ReturnCode.NxDomain;

                                Console.WriteLine(@"|- ChinaList - DNSPOD D+");
                            }
                        }

                        else if (ADnsSetting.WhiteListEnable && WhiteList.ContainsKey(dnsQuestion.Name) &&
                                 dnsQuestion.RecordType == RecordType.A)
                        {
                            if (ADnsSetting.DebugLog)
                                Console.WriteLine(@"|- WhiteList");

                            //WhiteList
                            ARecord blackRecord = new ARecord(dnsQuestion.Name, 10, WhiteList[dnsQuestion.Name]);
                            response.AnswerRecords.Add(blackRecord);
                        }

                        else
                        {
                            //Resolve
                            var (resolvedDnsList, statusCode) = ResolveOverHttps(clientAddress.ToString(),
                                dnsQuestion.Name.ToString(),
                                ADnsSetting.ProxyEnable, ADnsSetting.WProxy, dnsQuestion.RecordType);

                            var cacheItem = new CacheItem($"{dnsQuestion.Name}{dnsQuestion.RecordType}", resolvedDnsList);
                            if (!MemoryCache.Default.Contains(cacheItem.Key))
                                MemoryCache.Default.Add(cacheItem,
                                    new CacheItemPolicy { AbsoluteExpiration = DateTimeOffset.Now + TimeSpan.FromSeconds(resolvedDnsList[0].TimeToLive) });

                            if (resolvedDnsList != null && resolvedDnsList != new List<dynamic>())
                                foreach (var item in resolvedDnsList)
                                    response.AnswerRecords.Add(item);
                            else
                                response.ReturnCode = (ReturnCode) statusCode;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                response.ReturnCode = ReturnCode.ServerFailure;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(@"| " + ex);
                Console.ForegroundColor = OriginColor;
            }

            e.Response = response;
        }

        private static (List<dynamic> list, int statusCode) ResolveOverHttps(string clientIpAddress, string domainName,
            bool proxyEnable = false, IWebProxy wProxy = null, RecordType type = RecordType.A)
        {
            string dnsStr;
            List<dynamic> recordList = new List<dynamic>();

            using (WebClient webClient = new WebClient())
            {
                webClient.Headers["User-Agent"] = "AuroraDNSC/0.1";

                if (proxyEnable)
                    webClient.Proxy = wProxy;

                dnsStr = webClient.DownloadString(
                    ADnsSetting.HttpsDnsUrl +
                    @"?ct=application/dns-json&" +
                    $"name={domainName}&type={type.ToString().ToUpper()}&edns_client_subnet={clientIpAddress}");
            }

            JsonValue dnsJsonValue = Json.Parse(dnsStr);

            int statusCode = dnsJsonValue.AsObjectGetInt("Status");
            if (statusCode != 0)
                return (new List<dynamic>(), statusCode);

            if (dnsStr.Contains("\"Answer\""))
            {
                var dnsAnswerJsonList = dnsJsonValue.AsObjectGetArray("Answer");

                foreach (var itemJsonValue in dnsAnswerJsonList)
                {
                    string answerAddr = itemJsonValue.AsObjectGetString("data");
                    string answerDomainName = itemJsonValue.AsObjectGetString("name");
                    int answerType = itemJsonValue.AsObjectGetInt("type");
                    int ttl = itemJsonValue.AsObjectGetInt("TTL");

                    if (type == RecordType.A)
                    {
                        if (Convert.ToInt32(RecordType.A) == answerType)
                        {
                            ARecord aRecord = new ARecord(
                                DomainName.Parse(answerDomainName), ttl, IPAddress.Parse(answerAddr));

                            recordList.Add(aRecord);
                        }
                        else if (Convert.ToInt32(RecordType.CName) == answerType)
                        {
                            CNameRecord cRecord = new CNameRecord(
                                DomainName.Parse(answerDomainName), ttl, DomainName.Parse(answerAddr));

                            recordList.Add(cRecord);
                        }
                    }
                    else if (type == RecordType.Aaaa && ADnsSetting.IPv6Enable)
                    {
                        if (Convert.ToInt32(RecordType.Aaaa) == answerType)
                        {
                            AaaaRecord aaaaRecord = new AaaaRecord(
                                DomainName.Parse(answerDomainName), ttl, IPAddress.Parse(answerAddr));
                            recordList.Add(aaaaRecord);
                        }
                        else if (Convert.ToInt32(RecordType.CName) == answerType)
                        {
                            CNameRecord cRecord = new CNameRecord(
                                DomainName.Parse(answerDomainName), ttl, DomainName.Parse(answerAddr));
                            recordList.Add(cRecord);
                        }
                    }
                    else if (type == RecordType.CName && answerType == Convert.ToInt32(RecordType.CName))
                    {
                        CNameRecord cRecord = new CNameRecord(
                            DomainName.Parse(answerDomainName), ttl, DomainName.Parse(answerAddr));
                        recordList.Add(cRecord);
                    }
                    else if (type == RecordType.Ns && answerType == Convert.ToInt32(RecordType.Ns))
                    {
                        NsRecord nsRecord = new NsRecord(
                            DomainName.Parse(answerDomainName), ttl, DomainName.Parse(answerAddr));
                        recordList.Add(nsRecord);
                    }
                    else if (type == RecordType.Mx && answerType == Convert.ToInt32(RecordType.Mx))
                    {
                        MxRecord mxRecord = new MxRecord(
                            DomainName.Parse(answerDomainName), ttl,
                            ushort.Parse(answerAddr.Split(' ')[0]),
                            DomainName.Parse(answerAddr.Split(' ')[1]));
                        recordList.Add(mxRecord);
                    }
                    else if (type == RecordType.Txt && answerType == Convert.ToInt32(RecordType.Txt))
                    {
                        TxtRecord txtRecord = new TxtRecord(DomainName.Parse(answerDomainName), ttl, answerAddr);
                        recordList.Add(txtRecord);
                    }
                    else if (type == RecordType.Ptr && answerType == Convert.ToInt32(RecordType.Ptr))
                    {
                        PtrRecord ptrRecord = new PtrRecord(
                            DomainName.Parse(answerDomainName), ttl, DomainName.Parse(answerAddr));
                        recordList.Add(ptrRecord);
                    }
                }
            }

            return (recordList, statusCode);
        }

        public static List<dynamic> ResolveOverDNSPod(string domainName)
        {
            string dnsStr = new WebClient().DownloadString(
                $"http://119.29.29.29/d?dn={domainName}&ttl=1");
            if (string.IsNullOrWhiteSpace(dnsStr))
                return null;
            
            var ttlTime = Convert.ToInt32(dnsStr.Split(',')[1]);
            var dnsAnswerList = dnsStr.Split(',')[0].Split(';');

            return dnsAnswerList
                .Select(item => new ARecord(DomainName.Parse(domainName), ttlTime, IPAddress.Parse(item)))
                .Cast<dynamic>().ToList();
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

            string jSrt = File.ReadAllText(path);
            JsonValue configJson = Json.Parse(jSrt);

            ADnsSetting.ListenIp = jSrt.Contains("Listen")
                ? IPAddress.Parse(configJson.AsObjectGetString("Listen"))
                : IPAddress.Any;
            ADnsSetting.BlackListEnable =
                jSrt.Contains("BlackList") && configJson.AsObjectGetBool("BlackList");
            ADnsSetting.ChinaListEnable =
                jSrt.Contains("ChinaList") && configJson.AsObjectGetBool("ChinaList");
            ADnsSetting.WhiteListEnable =
                jSrt.Contains("RewriteList") && configJson.AsObjectGetBool("RewriteList");
            ADnsSetting.ProxyEnable =
                jSrt.Contains("ProxyEnable") && configJson.AsObjectGetBool("ProxyEnable");
            ADnsSetting.IPv6Enable =
                !jSrt.Contains("IPv6Enable") || configJson.AsObjectGetBool("IPv6Enable");
            ADnsSetting.AllowSelfSignedCert =
                jSrt.Contains("AllowSelfSignedCert") && configJson.AsObjectGetBool("AllowSelfSignedCert");
            ADnsSetting.TlsVersion =
                jSrt.Contains("TlsVersion") ? configJson.AsObjectGetFloat("Listen") : 0;
            ADnsSetting.EDnsCustomize =
                jSrt.Contains("EDnsCustomize") && configJson.AsObjectGetBool("EDnsCustomize");
            ADnsSetting.DebugLog =
                jSrt.Contains("DebugLog") && configJson.AsObjectGetBool("DebugLog");

            ADnsSetting.EDnsIp = jSrt.Contains("EDnsClientIp")
                ? IPAddress.Parse(configJson.AsObjectGetString("EDnsClientIp"))
                : IPAddress.Any;

            if (jSrt.Contains("HttpsDns"))
            {
                ADnsSetting.HttpsDnsUrl = configJson.AsObjectGetString("HttpsDns");
                if (string.IsNullOrEmpty(ADnsSetting.HttpsDnsUrl))
                    ADnsSetting.HttpsDnsUrl = "https://1.0.0.1/dns-query";
            }
            else
                ADnsSetting.HttpsDnsUrl = "https://1.0.0.1/dns-query";

            Console.WriteLine(@"Listen        : " + ADnsSetting.ListenIp);
            Console.WriteLine(@"BlackList     : " + ADnsSetting.BlackListEnable);
            Console.WriteLine(@"RewriteList   : " + ADnsSetting.WhiteListEnable);
            Console.WriteLine(@"ChinaList     : " + ADnsSetting.ChinaListEnable);
            Console.WriteLine(@"ProxyEnable   : " + ADnsSetting.ProxyEnable);
            Console.WriteLine(@"IPv6Enable    : " + ADnsSetting.IPv6Enable);
            Console.WriteLine(@"DebugLog      : " + ADnsSetting.DebugLog);
            Console.WriteLine(@"EDnsClient    : " + ADnsSetting.EDnsIp);
            Console.WriteLine(@"HttpsDns      : " + ADnsSetting.HttpsDnsUrl);
            Console.WriteLine(@"EDnsCustomize : " + ADnsSetting.EDnsCustomize);

            if (ADnsSetting.AllowSelfSignedCert)
                Console.WriteLine(@"AllowSelfSignedCert : " + ADnsSetting.AllowSelfSignedCert);

            if (ADnsSetting.TlsVersion != 0)
                Console.WriteLine(@"TlsVersion : " + ADnsSetting.TlsVersion);

            if (ADnsSetting.ProxyEnable)
            {
                ADnsSetting.WProxy = new WebProxy(configJson.AsObjectGetString("Proxy"));
                Console.WriteLine(@"ProxyServer : " + configJson.AsObjectGetString("Proxy"));
            }
        }

    }
}
