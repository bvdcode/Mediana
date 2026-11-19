// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Net;

namespace Mediana.Tests
{
    internal class UpdateHttpHandler(UpdateTestRelease release, Action? downloading = null,
        HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal) == false)
            {
                downloading?.Invoke();
            }
            HttpResponseMessage response = release.Respond(request);
            response.StatusCode = status;
            return Task.FromResult(response);
        }
    }
}
