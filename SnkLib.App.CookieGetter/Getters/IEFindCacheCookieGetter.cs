﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace SunokoLibrary.Application.Browsers
{
    /// <summary>
    /// IEのキャッシュファイルから直接Cookieを取得する。
    /// </summary>
    public class IEFindCacheCookieGetter : CookieGetterBase
    {
        //クラス命名センスとしてwininet.dllのFindNextUrlCacheEntryの文脈を用いる。
        //純粋にapi上で片付ける方法が不明なのでwininetのapi自体は使っていない。
        public IEFindCacheCookieGetter(BrowserConfig config)
            : base(config, PathType.Directory) { }
        public override bool IsAvailable
        {
            get
            {
                return string.IsNullOrEmpty(Config.CookiePath)
                    ? false : System.IO.Directory.Exists(Config.CookiePath);
            }
        }
        public override bool GetCookies(Uri targetUrl, CookieContainer container)
        {
            if (IsAvailable == false)
                return false;

            //関係のあるファイルだけ調べることによってパフォーマンスを向上させる
            var cookies = Directory.EnumerateFiles(Config.CookiePath, "*.txt")
                .Select(filePath => ReadAllTextIfHasSendableCookie(filePath, targetUrl))
                .Where(data => string.IsNullOrEmpty(data) == false)
                .SelectMany(data => ParseCookies(data))
                .ToList();

            //クッキーを有効期限で昇順に並び替えて、Expiresが最新のもので上書きされるようにする
            cookies.Sort((a, b) =>
                a == null && b == null ? 0 :
                a == null ? -1 :
                b == null ? 1 :
                a.Expires.CompareTo(b.Expires));

            foreach (var cookie in cookies)
                container.Add(cookie);
            return true;
        }
        public override ICookieImporter Generate(BrowserConfig config)
        { return new IEFindCacheCookieGetter(config); }
        /// <summary>
        /// IEのCookieテキストからCookieを取得する
        /// </summary>
        static IEnumerable<Cookie> ParseCookies(string cacheText)
        {
            var blocks = cacheText.Split('*');
            foreach (var block in blocks)
            {
                var lines = block.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (7 < lines.Length)
                {
                    var cookie = new Cookie();
                    var uri = new Uri("http://" + lines[2]);
                    cookie.Name = lines[0];
                    cookie.Value = lines[1];
                    cookie.Path = uri.AbsolutePath;
                    // ドメインの最初に.をつける
                    var domain = uri.Host;
                    if (domain.StartsWith("www."))
                        domain = domain.TrimStart(new char[] { 'w' });
                    if (domain.StartsWith(".") == false)
                        domain = '.' + domain;
                    cookie.Domain = domain;

                    // 有効期限を取得する
                    long uexp = 0, lexp = 0;
                    if (long.TryParse(lines[4], out lexp) && long.TryParse(lines[5], out uexp))
                        try
                        {
                            long ticks = ((long)uexp << 32) + lexp;
                            cookie.Expires = DateTime.FromFileTimeUtc(ticks);
                        }
                        catch (ArgumentOutOfRangeException ex)
                        { throw new CookieImportException("IEクッキーの解析に失敗しました。", ex); }
                    yield return cookie;
                }
            }
        }
        /// <summary>
        /// IEのCookieファイルを読み込む。この時、引数sendingTargetへ送信できるCookieが含まれるファイルのみ読み込む。
        /// </summary>
        /// <param name="cacheFilePath">Cookieファイル</param>
        /// <param name="sendingTarget">通信したいURL</param>
        /// <returns>Cookieファイル本文。</returns>
        static string ReadAllTextIfHasSendableCookie(string cacheFilePath, Uri sendingTarget)
        {
            Exception ex = null;
            Uri domainAndPath = null;
            try
            {
                using (var sr = new StreamReader(cacheFilePath, Encoding.GetEncoding("Shift_JIS")))
                {
                    string line;
                    var builder = new StringBuilder();
                    for (var lineIdx = 0; (line = sr.ReadLine()) != null; lineIdx++)
                    {
                        builder.AppendLine(line);
                        if (lineIdx >= 2 && Uri.TryCreate(string.Format("http://{0}", line), UriKind.Absolute, out domainAndPath))
                            break;
                    }
                    if (domainAndPath != null && sendingTarget.Host.EndsWith(domainAndPath.Host))
                    {
                        builder.Append(sr.ReadToEnd().Replace("\n", "\r\n"));
                        return builder.ToString();
                    }
                    else
                        return null;
                }
            }
            catch (IOException e) { ex = e; }
            catch (OutOfMemoryException e) { ex = e; }
            catch (System.Security.SecurityException e) { ex = e; }
            throw new CookieImportException("IEクッキーの参照に失敗しました。", ex);
        }
    }
}