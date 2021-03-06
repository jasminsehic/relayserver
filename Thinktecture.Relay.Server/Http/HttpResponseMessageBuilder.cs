using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Serilog;
using Thinktecture.Relay.Server.Dto;
using Thinktecture.Relay.Server.OnPremise;
using Thinktecture.Relay.Server.SignalR;

namespace Thinktecture.Relay.Server.Http
{
	internal class HttpResponseMessageBuilder : IHttpResponseMessageBuilder
	{
		private readonly ILogger _logger;
		private readonly IPostDataTemporaryStore _postDataTemporaryStore;
		private readonly Dictionary<string, Action<HttpContent, string>> _contentHeaderTransformation;

		public HttpResponseMessageBuilder(ILogger logger, IPostDataTemporaryStore postDataTemporaryStore)
		{
			_logger = logger;
			_postDataTemporaryStore = postDataTemporaryStore ?? throw new ArgumentNullException(nameof(postDataTemporaryStore));

			_contentHeaderTransformation = new Dictionary<string, Action<HttpContent, string>>()
			{
				["Content-Disposition"] = (r, v) => r.Headers.ContentDisposition = ContentDispositionHeaderValue.Parse(v),
				["Content-Length"] = (r, v) => r.Headers.ContentLength = Int64.Parse(v),
				["Content-Location"] = (r, v) => r.Headers.ContentLocation = new Uri(v),
				["Content-MD5"] = null,
				["Content-Range"] = null,
				["Content-Type"] = (r, v) => r.Headers.ContentType = MediaTypeHeaderValue.Parse(v),
				["Expires"] = (r, v) => r.Headers.Expires = (v == "-1" ? (DateTimeOffset?)null : new DateTimeOffset(DateTime.ParseExact(v, "R", CultureInfo.InvariantCulture))),
				["Last-Modified"] = (r, v) => r.Headers.LastModified = new DateTimeOffset(DateTime.ParseExact(v, "R", CultureInfo.InvariantCulture)),
			};
		}

		public HttpResponseMessage BuildFromConnectorResponse(IOnPremiseConnectorResponse response, Link link, string requestId)
		{
			var message = new HttpResponseMessage();

			if (response == null)
			{
				_logger?.Verbose("Received no response. request-id={RequestId}", requestId);

				message.StatusCode = HttpStatusCode.GatewayTimeout;
				message.Content = new ByteArrayContent(Array.Empty<byte>());
				message.Content.Headers.Add("X-TTRELAY-TIMEOUT", "On-Premise");
			}
			else
			{
				message.StatusCode = response.StatusCode;
				message.Content = GetResponseContentForOnPremiseTargetResponse(response, link);
				
				if (response.HttpHeaders.TryGetValue("WWW-Authenticate", out var wwwAuthenticate))
				{
					message.Headers.Add("WWW-Authenticate", wwwAuthenticate);
				}

				if (IsRedirectStatusCode(response.StatusCode) && response.HttpHeaders.TryGetValue("Location", out var location))
				{
					message.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
				}
			}

			return message;
		}

		public HttpContent GetResponseContentForOnPremiseTargetResponse(IOnPremiseConnectorResponse response, Link link)
		{
			if (response == null)
				throw new ArgumentNullException(nameof(response));

			if (response.StatusCode >= HttpStatusCode.InternalServerError && !link.ForwardOnPremiseTargetErrorResponse)
			{
				return null;
			}

			HttpContent content;

			if (response.ContentLength == 0)
			{
				_logger?.Verbose("Received empty body. request-id={RequestId}", response.RequestId);

				content = new ByteArrayContent(Array.Empty<byte>());
			}
			else if (response.Body != null)
			{
				// only legacy on-premise connectors (v1) use this property
				_logger?.Verbose("Received small legacy body with data. request-id={RequestId}, body-length={ResponseContentLength}", response.RequestId, response.Body.Length);

				content = new ByteArrayContent(response.Body);
			}
			else
			{
				_logger?.Verbose("Received body. request-id={RequestId}, content-length={ResponseContentLength}", response.RequestId, response.ContentLength);

				var stream = _postDataTemporaryStore.GetResponseStream(response.RequestId);
				if (stream == null)
				{
					throw new InvalidOperationException(); // TODO what now?
				}

				content = new StreamContent(stream, 0x10000);
			}

			AddContentHttpHeaders(content, response.HttpHeaders);

			return content;
		}

		public void AddContentHttpHeaders(HttpContent content, IReadOnlyDictionary<string, string> httpHeaders)
		{
			if (httpHeaders == null)
			{
				return;
			}

			foreach (var httpHeader in httpHeaders)
			{
				if (_contentHeaderTransformation.TryGetValue(httpHeader.Key, out var contentHeaderTranformation))
				{
					contentHeaderTranformation?.Invoke(content, httpHeader.Value);
				}
				else
				{
					content.Headers.TryAddWithoutValidation(httpHeader.Key, httpHeader.Value);
				}
			}
		}

		private static bool IsRedirectStatusCode(HttpStatusCode statusCode)
		{
			return ((int) statusCode >= 300) && ((int) statusCode <= 399);
		}
	}
}
