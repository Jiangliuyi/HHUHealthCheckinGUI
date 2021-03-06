﻿using HHUCheckin;
using HHUCheckin.Models;
using Jurassic.Library;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace HHUCheckin.Modules
{
    /// <summary>
    /// 打卡模块
    /// </summary>
    public class Checkin
    {
        const string BASE = "http://form.hhu.edu.cn/pdc/formDesignApi/S/xznuPIjG";
        const string API = "http://form.hhu.edu.cn/pdc/formDesignApi/dataFormSave";
        const int WIDLine = 7;
        const int UIDLine = 10;
        const int FillBeginLine = 118;
        const int FillEndLine = 120;

        /// <summary>
        /// CookieContainer
        /// </summary>
        private CookieContainer cookie { get; set; }


        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="cookie"></param>
        public Checkin(CookieContainer cookie)
        {
            this.cookie = cookie;
        }

        /// <summary>
        /// 进行打卡
        /// </summary>
        /// <returns></returns>
        public bool Do()
        {
            FrmMain.statusHandler?.Invoke(null, new Msg("正在进行打卡"));
            // 先GET一下BASE，获取到WID和UserID
            HttpClientHandler handler = new HttpClientHandler
            {
                CookieContainer = this.cookie,
                UseCookies = true
            };
            HttpClient client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/83.0.4103.106 Safari/537.36");
            HttpResponseMessage res;
            try
            {
                res = client.GetAsync(BASE).Result;
            }
            catch (TaskCanceledException e)
            {
                FrmMain.statusHandler?.Invoke(null, new Msg("打卡凭据获取失败，请检查网络链接"));
                return false;
            }
            // 把结果加载成HAP文档
            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(res.Content.ReadAsStringAsync().Result);
            var scripts = doc.DocumentNode.Descendants()
                             .Where(n => n.Name == "script");
            string fullScript = "";
            foreach (var t in scripts)
            {
                if (t.InnerHtml.Length > 0)
                {
                    fullScript = t.InnerHtml;
                    break;
                }
            }
            //string fullScript = doc.DocumentNode.Descendants()
            //                 .Where(n => n.Name == "script")
            //                 .First().InnerText;
            string[] fullScriptLines = fullScript.Split('\n');
            var engine = new Jurassic.ScriptEngine();
            object result = engine.Evaluate("(function() { " + fullScriptLines[WIDLine] + "\n return _selfFormWid; })()");
            string wid = result.ToString();
            result = engine.Evaluate("(function() { " + fullScriptLines[UIDLine] + "\n return _userId; })()");
            string uid = result.ToString();
            // 计算得到真正的API接口
            string readAPI = API + $"?wid={wid}&userId={uid}";
            // 取得往日填报信息
            StringBuilder sb = new StringBuilder();
            for (int i = FillBeginLine; i <= FillEndLine; i++)
                sb.Append(fullScriptLines[i]).Append("\n");
            result = engine.Evaluate("(function() { " + sb.ToString() + " return fillDetail; })()");
            string json = JSONObject.Stringify(engine, result);
            var fillDatas = JsonConvert.DeserializeObject<List<FillData>>(json);
            // 将填报信息转换成打卡信息
            var checkinData = Utils.ChangeType<FillData, CheckinData>(fillDatas[0]);
            var now = DateTime.Now;
            checkinData.DATETIME_CYCLE = now.ToString("yyyy/MM/dd");
            // 打卡
            var nvc = Utils.ConvertToKeyValuePairs<CheckinData>(checkinData);
            var req = new HttpRequestMessage(HttpMethod.Post, readAPI) { Content = new FormUrlEncodedContent(nvc) };
            try
            {
                res = client.SendAsync(req).Result;
            }
            catch (TaskCanceledException e)
            {
                FrmMain.statusHandler?.Invoke(null, new Msg("打卡失败，请检查网络链接"));
                return false;
            }
            if (res.StatusCode == HttpStatusCode.OK)
            {
                FrmMain.statusHandler?.Invoke(null, new Msg("打卡成功！"));
                return true;
            }
            else
            {
                FrmMain.statusHandler?.Invoke(null, new Msg("打卡失败，未知原因"));
                return false;
            }
        }
    }
}
