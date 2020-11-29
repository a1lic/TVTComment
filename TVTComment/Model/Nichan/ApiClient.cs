﻿
// 参考: http://prokusi.wiki.fc2.com/wiki/API%E4%BB%95%E6%A7%98

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Security.Cryptography;
using System.Net.Http;
using System.Net;
using System.Threading.Tasks;

namespace Nichan
{
    [System.Serializable]
    public class ApiClientException : Exception
    {
        public ApiClientException() { }
        public ApiClientException(string message) : base(message) { }
        public ApiClientException(string message, Exception inner) : base(message, inner) { }
        protected ApiClientException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }

    /// <summary>
    /// サーバーから返信がない（サーバーに接続できない）ときの例外
    /// </summary>
    public class NetworkApiClientException : ApiClientException
    {
        public NetworkApiClientException(Exception innerException) : base(null, innerException)
        {
        }
    }
    /// <summary>
    /// サーバーから返信が返ってきたが内容にエラーがあるときの例外
    /// </summary>
    public class ResponseApiClientException : ApiClientException
    {
    }
    /// <summary>
    /// サーバーでの認証に問題があるときの例外
    /// </summary>
    public class AuthorizationApiClientException : ResponseApiClientException
    {
    }


    public class ApiClient
    {
        private static readonly Random random = new Random();
        private const string ctChars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

        public string HmKey { get; }
        public string AppKey { get; }
        public string UserId { get; }
        public string Password { get; }

        private string sessionID = "";

        private readonly HttpClient httpClient = new HttpClient(
            new HttpClientHandler() {
                AutomaticDecompression = DecompressionMethods.GZip
            }
        );

        public ApiClient(string hmKey, string appKey, string userId = "", string password = "")
        {
            this.HmKey = hmKey;
            this.AppKey = appKey;
            this.UserId = userId;
            this.Password = password;
        }

        private string getHash(string message)
        {
            using (HMACSHA256 hs256 = new HMACSHA256(Encoding.UTF8.GetBytes(this.HmKey)))
            {
                byte[] hash = hs256.ComputeHash(Encoding.UTF8.GetBytes(message));
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        private async Task authorize()
        {
            //string ct = string.Join("", Enumerable.Range(0, 10).Select(_ => ctChars[random.Next(ctChars.Length)]));

            //string message = this.appKey + ct;
            //string hb =this.getHash(message);

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.5ch.net/v1/auth/");
            request.Headers.TryAddWithoutValidation("User-Agent", "");
            request.Headers.TryAddWithoutValidation("X-2ch-UA", "JaneStyle/3.80");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                { "KY", this.AppKey }, { "ID", this.UserId}, { "PW", this.Password },
            });
            HttpResponseMessage response;
            try
            {
                response = await this.httpClient.SendAsync(request);
            }
            catch(HttpRequestException e)
            {
                throw new NetworkApiClientException(e);
            }

            if(response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new AuthorizationApiClientException();
            }
            string responseContent = await response.Content.ReadAsStringAsync();
            int idx = responseContent.IndexOf(':');
            if(idx == -1)
            {
                this.sessionID = "";
                throw new AuthorizationApiClientException();
            }
            this.sessionID = responseContent[(idx + 1)..];
        }

        public async Task<string> GetDat(string server, string board, string threadID)
        {
            if(this.sessionID == "")
            {
                await this.authorize();
            }
            string message = $"/v1/{server}/{board}/{threadID}{this.sessionID}{this.AppKey}";
            string hobo = this.getHash(message);

            var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.5ch.net/v1/{server}/{board}/{threadID}");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (compatible; JaneStyle/3.80..)");
            request.Headers.TryAddWithoutValidation("Connection", "close");
            request.Content = new FormUrlEncodedContent(
                new Dictionary<string, string> { { "sid", this.sessionID }, { "hobo", hobo }, { "appkey", this.AppKey } }
            );

            async Task<HttpResponseMessage> post()
            {
                HttpResponseMessage response;
                try
                {
                    response = await this.httpClient.SendAsync(request);
                }
                catch (HttpRequestException e)
                {
                    throw new NetworkApiClientException(e);
                }
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new AuthorizationApiClientException();
                }
                return response;
            }

            HttpResponseMessage response;
            try
            {
                response = await post();
            }
            catch(AuthorizationApiClientException)
            {
                await this.authorize();
                response = await post();
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                string responseBody = Encoding.GetEncoding(932).GetString(
                    await response.Content.ReadAsByteArrayAsync()
                );
                throw new ResponseApiClientException();
            }

            string responseContent = Encoding.GetEncoding(932).GetString(
                await response.Content.ReadAsByteArrayAsync()
            );
            return responseContent;
        }
    }
}