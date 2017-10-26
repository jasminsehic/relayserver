using Autofac;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using Newtonsoft.Json;
using Thinktecture.Relay.Server.Communication;
using Thinktecture.Relay.Server.OnPremise;
using Thinktecture.Relay.Server.SignalR;

namespace Thinktecture.Relay.Server.Controller
{
	[Authorize(Roles = "OnPremise")]
	[OnPremiseConnectionModuleBindingFilter]
	public class ResponseController : ApiController
	{
		private readonly ILogger _logger;
		private readonly IPostDataTemporaryStore _postDataTemporaryStore;
		private readonly IBackendCommunication _backendCommunication;

		public ResponseController(IBackendCommunication backendCommunication, ILogger logger, IPostDataTemporaryStore postDataTemporaryStore)
		{
			_logger = logger;
			_backendCommunication = backendCommunication ?? throw new ArgumentNullException(nameof(backendCommunication));
			_postDataTemporaryStore = postDataTemporaryStore ?? throw new ArgumentNullException(nameof(postDataTemporaryStore));
		}

		public async Task<IHttpActionResult> Forward()
		{
			var message = JToken.Parse(Request.Headers.TryGetValues("X-TTRELAY-METADATA", out var headerValues) ? headerValues.First() : await Request.Content.ReadAsStringAsync().ConfigureAwait(false));

			var response = message.ToObject<OnPremiseConnectorResponse>();

			if (headerValues == null)
			{
				// this is a legacy on premise connector (v1)
				if (response.Body?.Length >= 0x10000)
				{
					_postDataTemporaryStore.SaveResponse(response.RequestId, response.Body);
					response.Body = null; // free the memory a.s.a.p.
				}

				response.ContentLength = response.Body?.Length ?? 0;
			}
			else
			{
				using (var stream = _postDataTemporaryStore.CreateResponseStream(response.RequestId))
				{
					var requestStream = await Request.Content.ReadAsStreamAsync().ConfigureAwait(false);
					await requestStream.CopyToAsync(stream).ConfigureAwait(false);

					response.ContentLength = stream.Length;
				}
			}

			_logger?.Trace("Received on-premise response. request-id={0}, message={1}", response.RequestId, message);

			await _backendCommunication.SendOnPremiseTargetResponse(response.OriginId, response).ConfigureAwait(false);

			return Ok();
		}
	}
}
