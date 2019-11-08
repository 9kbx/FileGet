using NLog;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FileGet
{
    class Program
    {
        static Logger logger = LogManager.GetCurrentClassLogger();
        static ConcurrentQueue<string> _NameQueue = new ConcurrentQueue<string>();
        static ConcurrentDictionary<string, bool> _UrlCompleted = new ConcurrentDictionary<string, bool>();
        static ConcurrentDictionary<string, string> _Urls = new ConcurrentDictionary<string, string>();
        static string _SaveDir = string.Empty;
        static int _ThreadNum = 1;
        static string _UserAgent = string.Empty;
        static string _DownloadFiles = string.Empty;

        static void Main(string[] args)
        {
            _ThreadNum = Convert.ToInt32(ConfigurationManager.AppSettings["ThreadNum"]);
            _UserAgent = ConfigurationManager.AppSettings["UserAgent"];
            _SaveDir = ConfigurationManager.AppSettings["SaveDir"];
            _DownloadFiles = AppDomain.CurrentDomain.BaseDirectory + "下载地址.txt";

            var cnt = System.IO.File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "readme.txt",Encoding.UTF8);
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine(cnt);
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine();

            if (System.IO.File.Exists(_DownloadFiles))
            {
                Init();
                 
                if (_Urls.Count > 0)
                {
                    var log = ($"总共 {_Urls.Count} 个链接要下载\r\n按“任意键”开始下载文件，“esc”取消下载。");

                    Console.WriteLine(log);
                    logger.Debug(log);
                    var sb = new StringBuilder();
                    foreach (var name in _Urls.Keys)
                    {
                        sb.AppendLine($"{name}\t{_Urls[name]}");
                    }
                    logger.Debug(sb.ToString());
                    var k = Console.ReadKey();

                    if (k.Key != ConsoleKey.Escape)
                    {
                        Console.WriteLine("开始下载！");
                        
                        DoStart();

                        Console.WriteLine("文件下载完成，文件保存在：");
                        Console.WriteLine(_SaveDir);

                        System.Diagnostics.Process.Start("explorer.exe", _SaveDir);
                    }
                    else
                    {
                        Console.WriteLine("取消下载！");
                    }
                }
                else
                {
                    Console.WriteLine("没有要下载的数据！");
                }
            }
            else
            {
                Console.WriteLine("没有要下载的数据！");
            }

            Console.WriteLine();
            Console.WriteLine("按任意键退出程序！");
            Console.ReadLine();
        }

        static void Init()
        {
            #region 文件夹
            try
            {
                if (string.IsNullOrWhiteSpace(_SaveDir))
                {
                    _SaveDir = AppDomain.CurrentDomain.BaseDirectory + @"download\";
                }
                else
                {
                    if (!_SaveDir.EndsWith(@"\"))
                        _SaveDir += @"\"; 

                    _SaveDir = _SaveDir + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + @"\";

                    if (!System.IO.Path.IsPathRooted(_SaveDir))
                        _SaveDir = AppDomain.CurrentDomain.BaseDirectory + _SaveDir;
                }

                if (!System.IO.Directory.Exists(_SaveDir))
                    System.IO.Directory.CreateDirectory(_SaveDir);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "生成文件夹时发生异常");
                Console.WriteLine($"生成文件夹时发生异常：{_SaveDir}");
                Console.WriteLine("按任意键退出程序！");
                Console.ReadKey();
                Environment.Exit(0);
            }
            #endregion 文件夹

            #region 文件
            var arr = System.IO.File.ReadAllLines(_DownloadFiles, Encoding.UTF8);
            var urls = new Dictionary<string, string>();
            var hash = new Hashtable();
            foreach (var str in arr)
            {
                if (!string.IsNullOrEmpty(str) && !str.Trim().StartsWith("#"))
                {
                    var name = string.Empty;
                    var url = string.Empty;
                    var arr2 = str.Split('|');
                    if (arr2.Length > 1)
                    {
                        name = arr2[0].Trim();
                        url = arr2[1].Trim();
                    }
                    else
                    {
                        url = arr2[0];
                    }

                    if (string.IsNullOrWhiteSpace(name))
                        name = System.IO.Path.GetFileNameWithoutExtension(url);

                    // 文件名过滤非法字符
                    name = name.Replace("\"", "")
                               .Replace("\\", "")
                               .Replace("/", "")
                               .Replace(":", "")
                               .Replace("*", "")
                               .Replace("?", "")
                               .Replace("<", "")
                               .Replace(">", "")
                               .Replace("|", "");

                    if (!hash.ContainsKey(name))
                    {
                        hash.Add(name, null);
                        urls.Add(name, url);
                    }
                }
            }

            if (urls.Count > 0)
            {
                _UrlCompleted.Clear();
                _Urls.Clear();
                foreach (var name in urls.Keys)
                {
                    _UrlCompleted.TryAdd(name, false);
                    _Urls.TryAdd(name, urls[name]);
                    _NameQueue.Enqueue(name);
                }
            }
            #endregion 文件
        }

        static void DoStart()
        {
            if (_ThreadNum > 1)
            {
                for (int i = 0; i < _ThreadNum; i++)
                {
                    new Thread(DownloadFile).Start();
                }
            }
            else
            {
                DownloadFile();
            } 
            while (_UrlCompleted.Count(a => a.Value == false) > 0)
                Thread.Sleep(1000);
        }

        static void DownloadFile()
        {
            var name = string.Empty;
            var url = string.Empty;
            var http = new RestSharp.RestClient();
            http.UserAgent = _UserAgent;

            while (!_NameQueue.IsEmpty)
            {
                _NameQueue.TryDequeue(out name);
                url = _Urls[name];

                http.BaseUrl = new Uri(url);
                if (http.BaseUrl.Scheme == "https")
                    System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                else
                    System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls;

                var req = new RestSharp.RestRequest(RestSharp.Method.GET);
                req.AddParameter("Accept", "application/json, text/javascript, */*; q=0.01", RestSharp.ParameterType.HttpHeader);
                req.AddParameter("Accept-Language", "en", RestSharp.ParameterType.HttpHeader);
                req.AddParameter("Accept-Encoding", "gzip, deflate, br", RestSharp.ParameterType.HttpHeader);

                try
                {
                    var data = http.DownloadData(req);
                    if (data != null && data.Length > 0)
                    {
                        //var ext = System.IO.Path.GetExtension(url);
                        var ext = System.IO.Path.GetExtension(new Uri(url).LocalPath);
                        var fileName = name;
                        var file = $"{_SaveDir}{fileName}{ext}";
                        System.IO.File.WriteAllBytes(file, data);

                        Console.WriteLine($"[下载成功]{url}");
                        logger.Debug($@"文件下载成功：{name}\t{url}");
                    }
                    else
                    {
                        Console.WriteLine($"[x下载失败]{url}");
                        logger.Debug($@"文件下载失败：{name}\t{url}");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $@"文件下载异常：{name}\t{url}"); 
                    Console.WriteLine($"[x下载异常]{url}");
                }
                finally
                {
                    _UrlCompleted[name] = true;
                }
                Thread.Sleep(300); 
            }
        }
    }
}
