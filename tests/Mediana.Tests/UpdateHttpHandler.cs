// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Net;

namespace Mediana.Tests
{
    internal class UpdateHttpHandler(string metadata, byte[] payload, Action? downloading = null,
        HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            HttpResponseMessage response = new(status);
            if (request.RequestUri?.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal) == true)
            {
                response.Content = new StringContent(metadata);
            }
            else
            {
                downloading?.Invoke();
                response.Content = new ByteArrayContent(payload);
            }
            return Task.FromResult(response);
        }
    }
}
