﻿#region License
//   Copyright 2010 John Sheehan
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License. 
#endregion

using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using RestSharp.Deserializers;
using RestSharp.Extensions;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Http;
using System.Collections.ObjectModel;

namespace RestSharp
{
	/// <summary>
	/// Client to translate RestRequests into Http requests and process response result
	/// </summary>
	public partial class RestClient : IRestClient
    {
        #region Private Members 

		static readonly Version version = new AssemblyName(Assembly.GetExecutingAssembly().FullName).Version;
            
        #endregion

        //public IHttpFactory HttpFactory = new SimpleHttpFactory<Http>();

        #region Public Constructors

        /// <summary>
		/// Default constructor that registers default content handlers
		/// </summary>
		public RestClient()
		{
            ContentHandlers = new Dictionary<string, IDeserializer>();
			AcceptTypes = new List<string>();
			DefaultParameters = new List<Parameter>();

			// register default handlers
			AddHandler("application/json", new JsonDeserializer());
			AddHandler("application/xml", new XmlDeserializer());
			AddHandler("text/json", new JsonDeserializer());
			AddHandler("text/x-json", new JsonDeserializer());
			AddHandler("text/javascript", new JsonDeserializer());
			AddHandler("text/xml", new XmlDeserializer());
			AddHandler("*", new JsonDeserializer());

			FollowRedirects = true;
		}

		/// <summary>
		/// Sets the BaseUrl property for requests made by this client instance
		/// </summary>
		/// <param name="baseUrl"></param>
		public RestClient(string baseUrl)
			: this()
		{
			BaseUrl = baseUrl;
		}

        #endregion

        #region Public Properties

        /// <summary>
        /// A ReadOnlyCollection of the default Accept header values used with no Accept header value is set explicitly.
        /// </summary>
        public ICollection<string> DefaultAcceptTypes { get { return new ReadOnlyCollection<string>(AcceptTypes); } }

        /// <summary>
		/// Parameters included with every request made with this instance of RestClient
		/// If specified in both client and request, the request wins
		/// </summary>
		public IList<Parameter> DefaultParameters { get; private set; }

        /// <summary>
        /// Maximum number of redirects to follow if FollowRedirects is true
        /// </summary>
        public int? MaxRedirects { get; set; }

        /// <summary>
        /// Default is true. Determine whether or not requests that result in 
        /// HTTP status codes of 3xx should follow returned redirect
        /// </summary>
        public bool FollowRedirects { get; set; }

        /// <summary>
        /// X509CertificateCollection to be sent with request
        /// </summary>
        //public X509CertificateCollection ClientCertificates { get; set; }

        /// <summary>
        /// Proxy to use for requests made by this client instance.
        /// Passed on to underlying WebRequest if set.
        /// </summary>
        public IWebProxy Proxy { get; set; }

        /// <summary>
        /// The CookieContainer used for requests made by this client instance
        /// </summary>
        public CookieContainer CookieContainer { get; set; }

        /// <summary>
        /// UserAgent to use for requests made by this client instance
        /// </summary>
        private string _userAgent;
        public string UserAgent
        {
            get 
            {
                if (!this._userAgent.HasValue())
                {
                    this._userAgent = "RestSharp/" + version;
                }

                return this._userAgent;
            }
            set
            {
                this._userAgent = value;
            }
        }

        /// <summary>
        /// Timeout in milliseconds to use for requests made by this client instance
        /// </summary>
        public int? Timeout { get; set; }

        /// <summary>
        /// Authenticator to use for requests made by this client instance
        /// </summary>
        public IAuthenticator Authenticator { get; set; }

        private string _baseUrl;
        /// <summary>
        /// Combined with Request.Resource to construct URL for request
        /// Should include scheme and domain without trailing slash.
        /// </summary>
        /// <example>
        /// client.BaseUrl = "http://example.com";
        /// </example>
        public virtual string BaseUrl
        {
            get
            {
                return _baseUrl;
            }
            set
            {
                _baseUrl = value;
                if (_baseUrl != null && _baseUrl.EndsWith("/"))
                {
                    _baseUrl = _baseUrl.Substring(0, _baseUrl.Length - 1);
                }
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
		/// Registers a content handler to process response content
		/// </summary>
		/// <param name="contentType">MIME content type of the response content</param>
		/// <param name="deserializer">Deserializer to use to process content</param>
		public void AddHandler(string contentType, IDeserializer deserializer)
		{
			ContentHandlers[contentType] = deserializer;
			if (contentType != "*")
			{
				AcceptTypes.Add(contentType);
				// add Accept header based on registered deserializers
				var accepts = string.Join(", ", AcceptTypes.ToArray());
				this.RemoveDefaultParameter("Accept");
				this.AddDefaultParameter("Accept", accepts, ParameterType.HttpHeader);
			}
		}

		/// <summary>
		/// Remove a content handler for the specified MIME content type
		/// </summary>
		/// <param name="contentType">MIME content type to remove</param>
		public void RemoveHandler(string contentType)
		{
			ContentHandlers.Remove(contentType);
			AcceptTypes.Remove(contentType);
			this.RemoveDefaultParameter("Accept");
		}

		/// <summary>
		/// Remove all content handlers
		/// </summary>
		public void ClearHandlers()
		{
			ContentHandlers.Clear();
			AcceptTypes.Clear();
			this.RemoveDefaultParameter("Accept");
		}

		/// <summary>
		/// Retrieve the handler for the specified MIME content type
		/// </summary>
		/// <param name="contentType">MIME content type to retrieve</param>
		/// <returns>IDeserializer instance</returns>
		IDeserializer GetHandler(string contentType)
		{
			if (string.IsNullOrEmpty(contentType) && ContentHandlers.ContainsKey("*"))
			{
				return ContentHandlers["*"];
			}

			var semicolonIndex = contentType.IndexOf(';');
			if (semicolonIndex > -1) contentType = contentType.Substring(0, semicolonIndex);
			IDeserializer handler = null;
			if (ContentHandlers.ContainsKey(contentType))
			{
				handler = ContentHandlers[contentType];
			}
			else if (ContentHandlers.ContainsKey("*"))
			{
				handler = ContentHandlers["*"];
			}

			return handler;
		}

        /// <summary>
        /// Executes a GET-style request asynchronously, authenticating if needed
        /// </summary>
        /// <typeparam name="T">Target deserialization type</typeparam>
        /// <param name="request">Request to be executed</param>
        public virtual async Task<IRestResponse<T>> ExecuteGetAsync<T>(IRestRequest request)
        {
            return await ExecuteGetAsync<T>(request, CancellationToken.None);
        }

        /// <summary>
        /// Executes a GET-style request asynchronously, authenticating if needed
        /// </summary>
        /// <typeparam name="T">Target deserialization type</typeparam>
        /// <param name="request">Request to be executed</param>
        /// <param name="token">The cancellation token</param>
        public virtual async Task<IRestResponse<T>> ExecuteGetAsync<T>(IRestRequest request, CancellationToken token)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            request.Method = Method.GET;
            return await ExecuteAsync<T>(request, token);
        }

        /// <summary>
        /// Executes a POST-style request asynchronously, authenticating if needed
        /// </summary>
        /// <typeparam name="T">Target deserialization type</typeparam>
        /// <param name="request">Request to be executed</param>
        public virtual async Task<IRestResponse<T>> ExecutePostAsync<T>(IRestRequest request)
        {
            return await ExecutePostAsync<T>(request, CancellationToken.None);
        }

        /// <summary>
        /// Executes a POST-style request asynchronously, authenticating if needed
        /// </summary>
        /// <typeparam name="T">Target deserialization type</typeparam>
        /// <param name="request">Request to be executed</param>
        /// <param name="token">The cancellation token</param>
        public virtual async Task<IRestResponse<T>> ExecutePostAsync<T>(IRestRequest request, CancellationToken token)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            request.Method = Method.POST;
            return await ExecuteAsync<T>(request, token);
        }

        /// <summary>
        /// Executes the request asynchronously, authenticating if needed
        /// </summary>
        /// <typeparam name="T">Target deserialization type</typeparam>
        /// <param name="request">Request to be executed</param>
        public virtual async Task<IRestResponse<T>> ExecuteAsync<T>(IRestRequest request)
        {
            return await ExecuteAsync<T>(request, CancellationToken.None);
        }

        /// <summary>
        /// Executes the request asynchronously, authenticating if needed
        /// </summary>
        /// <typeparam name="T">Target deserialization type</typeparam>
        /// <param name="request">Request to be executed</param>
        /// <param name="token">The cancellation token</param>
        public virtual async Task<IRestResponse<T>> ExecuteAsync<T>(IRestRequest request, CancellationToken token)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            return Deserialize<T>(request, await ExecuteAsync(request));
        }

        /// <summary>
        /// Executes the request asynchronously, authenticating if needed
        /// </summary>
        /// <param name="request">Request to be executed</param>
        public virtual async Task<IRestResponse> ExecuteAsync(IRestRequest request)
        {
            return await ExecuteAsync(request, CancellationToken.None);
        }

        /// <summary>
        /// Executes the request asynchronously, authenticating if needed
        /// </summary>
        /// <param name="request">Request to be executed</param>
        /// <param name="token">The cancellation token</param>
        public virtual async Task<IRestResponse> ExecuteAsync(IRestRequest request, CancellationToken token)
        {
            HttpMethod method = new HttpMethod(Enum.GetName(typeof(Method), request.Method));
            switch (request.Method)
            {
                case Method.PATCH:
                case Method.POST:
                case Method.PUT:
                    return await ExecuteAsync(request, method, DoAsPostAsync);
                default:
                    return await ExecuteAsync(request, method, DoAsGetAsync);
            }           
        }

        /// <summary>
        /// Executes the specified request and downloads the response data
        /// </summary>
        /// <param name="request">Request to execute</param>
        /// <returns>Response data</returns>
        public async Task<byte[]> DownloadDataAsync(IRestRequest request)
        {
            var response = await ExecuteAsync(request);
            return response.RawBytes;
        }

        #endregion

        #region Private Properties

        private List<string> AcceptTypes { get; set; }

        private IDictionary<string, IDeserializer> ContentHandlers { get; set; }

        #endregion 

        #region Private Methods 

        private async Task<IRestResponse> ExecuteAsync(IRestRequest restRequest, HttpMethod httpMethod, Func<IHttp, HttpMethod, Task<HttpResponse>> getResponse)
        {
			//AddAuthenticationIfNeeded(restRequest);

			IRestResponse response = new RestResponse();

            try
			{
				//var http = HttpFactory.Create();

                //http.HandlerFactory = this.HandlerFactory();                               
                
                //ConfigureHttp(request, http);

                //response = ConvertToRestResponse(request, httpResponse); //execute async

                var converter = new HttpConverter();

                var httpRequest = converter.ConvertTo(this, restRequest);

                IHttp http = new Http(httpRequest);

                var httpResponse = await getResponse(http, httpMethod);

                response = converter.ConvertFrom(httpResponse);
                response.Request = restRequest;
				response.Request.IncreaseNumAttempts();
			}
			catch (Exception ex)
			{
				response.ResponseStatus = ResponseStatus.Error;
				response.ErrorMessage = ex.Message;
				response.ErrorException = ex;
			}

			return response;
        }



        //private void AuthenticateIfNeeded(RestClient client, IRestRequest request)
        //{
        //    if (Authenticator != null)
        //    {
        //        Authenticator.Authenticate(client, request);
        //    }
        //}

        private static async Task<HttpResponse> DoAsGetAsync(IHttp http, HttpMethod method)
        {
            return await http.AsGetAsync(method);
        }

        private static async Task<HttpResponse> DoAsPostAsync(IHttp http, HttpMethod method)
        {
            return await http.AsPostAsync(method);
        }

        //private IRestResponse ProcessResponse(IRestRequest request, HttpResponse httpResponse)
        //{
        //    var restResponse = ConvertToRestResponse(request, httpResponse);
        //    return restResponse;
        //}

        //private static string EncodeParameters(IEnumerable<Parameter> parameters)
        //{
        //    var querystring = new StringBuilder();
        //    foreach (var p in parameters)
        //    {
        //        if (querystring.Length > 1)
        //            querystring.Append("&");
        //        querystring.AppendFormat("{0}={1}", p.Name.UrlEncode(), (p.Value.ToString()).UrlEncode());
        //    }

        //    return querystring.ToString();
        //}

        //private void ConfigureHttp(IRestRequest request, IHttp http)
        //{

        //    http.AlwaysMultipartFormData = request.AlwaysMultipartFormData;
        //    http.UseDefaultCredentials = request.UseDefaultCredentials;
        //    http.ResponseWriter = request.ResponseWriter;

        //    http.CookieContainer = CookieContainer;

        //    // move RestClient.DefaultParameters into Request.Parameters
        //    //foreach (var p in DefaultParameters)
        //    //{
        //    //    if (request.Parameters.Any(p2 => p2.Name == p.Name && p2.Type == p.Type))
        //    //    {
        //    //        continue;
        //    //    }

        //    //    request.AddParameter(p);
        //    //}

        //    // Add Accept header based on registered deserializers if none has been set by the caller.
        //    if (request.Parameters.All(p2 => p2.Name.ToLowerInvariant() != "accept"))
        //    {
        //        var accepts = string.Join(", ", AcceptTypes.ToArray());
        //        request.AddParameter("Accept", accepts, ParameterType.HttpHeader);
        //    }

        //    http.Url = BuildUri(request);

        //    var userAgent = UserAgent ?? http.UserAgent;
        //    http.UserAgent = userAgent.HasValue() ? userAgent : "RestSharp/" + version;

        //    var timeout = request.Timeout > 0 ? request.Timeout : Timeout;
        //    if (timeout > 0)
        //    {
        //        http.Timeout = timeout;
        //    }

        //    http.FollowRedirects = FollowRedirects;

        //    //if (ClientCertificates != null)
        //    //{
        //    //    http.ClientCertificates = ClientCertificates;
        //    //}

        //    http.MaxRedirects = MaxRedirects;

        //    if (request.Credentials != null)
        //    {
        //        http.Credentials = request.Credentials;
        //    }

        //    var headers = from p in request.Parameters
        //                  where p.Type == ParameterType.HttpHeader
        //                  select new HttpHeader
        //                  {
        //                      Name = p.Name,
        //                      Value = new List<string>() { p.Value.ToString() }
        //                  };

        //    foreach (var header in headers)
        //    {
        //        http.Headers.Add(header);
        //    }

        //    var cookies = from p in request.Parameters
        //                  where p.Type == ParameterType.Cookie
        //                  select new HttpCookie
        //                  {
        //                      Name = p.Name,
        //                      Value = p.Value.ToString()
        //                  };

        //    foreach (var cookie in cookies)
        //    {
        //        http.Cookies.Add(cookie);
        //    }

        //    var @params = request.Parameters
        //                        .Where(p => p.Type == ParameterType.GetOrPost && p.Value != null)
        //                        .Select(p => new KeyValuePair<string, string>(p.Name, p.Value.ToString()));

        //    //var @params = from p in request.Parameters
        //    //              where p.Type == ParameterType.GetOrPost
        //    //                    && p.Value != null
        //    //              select new KeyValuePair<string,string>()
        //    //              {
        //    //                  Key = p.Name,
        //    //                  Value = p.Value.ToString()
        //    //              };

        //    //var t = KeyValuePair<string, string>();

        //    foreach (var parameter in @params)
        //    {
        //        http.Parameters.Add(new KeyValuePair<string,string>(parameter.Key, parameter.Value));
        //    }

        //    foreach (var file in request.Files)
        //    {
        //        //http.Files.Add(new HttpFile { Name = file.Name, ContentType = file.ContentType, Writer = file.Writer, FileName = file.FileName, ContentLength = file.ContentLength });
        //        http.Files.Add(new HttpFile { Name = file.Name, ContentType = file.ContentType, Data = file.Data, FileName = file.FileName, ContentLength = file.ContentLength });
        //    }

        //    var body = (from p in request.Parameters
        //                where p.Type == ParameterType.RequestBody
        //                select p).FirstOrDefault();

        //    if (body != null)
        //    {
        //        object val = body.Value;
        //        if (val is byte[])
        //            http.RequestBodyBytes = (byte[])val;
        //        else
        //            http.RequestBody = body.Value.ToString();
        //        http.RequestContentType = body.Name;
        //    }

        //    ConfigureProxy(http);
        //}

        //private void ConfigureProxy(IHttp http)
        //{
        //    if (Proxy != null)
        //    {
        //        http.Proxy = Proxy;
        //    }
        //}

        //private RestResponse ConvertToRestResponse(IRestRequest request, HttpResponse httpResponse)
        //{
        //    var restResponse = new RestResponse();
        //    restResponse.Content = httpResponse.Content;
        //    restResponse.ContentEncoding = httpResponse.ContentEncoding;
        //    restResponse.ContentLength = httpResponse.ContentLength;
        //    restResponse.ContentType = httpResponse.ContentType;
        //    restResponse.ErrorException = httpResponse.ErrorException;
        //    restResponse.ErrorMessage = httpResponse.ErrorMessage;
        //    restResponse.RawBytes = httpResponse.RawBytes;
        //    restResponse.ResponseStatus = httpResponse.ResponseStatus;
        //    restResponse.ResponseUri = httpResponse.ResponseUri;
        //    restResponse.Server = httpResponse.Server;
        //    restResponse.StatusCode = httpResponse.StatusCode;
        //    restResponse.StatusDescription = httpResponse.StatusDescription;
        //    restResponse.Request = request;

        //    foreach (var header in httpResponse.Headers)
        //    {
        //        restResponse.Headers.Add(new Parameter { Name = header.Name, Value = header.Value, Type = ParameterType.HttpHeader });
        //    }

        //    foreach (var cookie in httpResponse.Cookies)
        //    {
        //        restResponse.Cookies.Add(new RestResponseCookie
        //        {
        //            Comment = cookie.Comment,
        //            CommentUri = cookie.CommentUri,
        //            Discard = cookie.Discard,
        //            Domain = cookie.Domain,
        //            Expired = cookie.Expired,
        //            Expires = cookie.Expires,
        //            HttpOnly = cookie.HttpOnly,
        //            Name = cookie.Name,
        //            Path = cookie.Path,
        //            Port = cookie.Port,
        //            Secure = cookie.Secure,
        //            TimeStamp = cookie.TimeStamp,
        //            Value = cookie.Value,
        //            Version = cookie.Version
        //        });
        //    }

        //    return restResponse;
        //}

		private IRestResponse<T> Deserialize<T>(IRestRequest request, IRestResponse raw)
		{
			request.OnBeforeDeserialization(raw);

			IRestResponse<T> response = new RestResponse<T>();
			try
			{
				response = raw.toAsyncResponse<T>();
				response.Request = request;

				// Only attempt to deserialize if the request has not errored due
				// to a transport or framework exception.  HTTP errors should attempt to 
				// be deserialized 

				if (response.ErrorException==null) 
				{
                    string mediaType = (raw.ContentType != null) ? raw.ContentType.MediaType : String.Empty;

                    IDeserializer handler = GetHandler(mediaType);
					handler.RootElement = request.RootElement;
					handler.DateFormat = request.DateFormat;
					handler.Namespace = request.XmlNamespace;

					response.Data = handler.Deserialize<T>(raw);
				}
			}
			catch (Exception ex)
			{
				response.ResponseStatus = ResponseStatus.Error;
				response.ErrorMessage = ex.Message;
				response.ErrorException = ex;
			}

			return response;
        }

        #endregion
    }
}