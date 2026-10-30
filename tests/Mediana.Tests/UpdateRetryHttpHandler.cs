// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Net;

namespace Mediana.Tests
{
    internal class UpdateRetryHttpHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request, ++RequestCount));
        }

        public static HttpResponseMessage Metadata(byte[] payload)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(UpdateTestRelease.Metadata("1.2.3", payload).ToJsonString())
            };
        }
    }
}
