// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RequestThrottling;
using Microsoft.AspNetCore.RequestThrottling.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Aspnetcore.RequestThrottling
{
    /// <summary>
    /// Limits the flow of requests through your application,
    /// hopefully decreasing congestion by preventing threadpool starvation.
    /// </summary>
    public class RequestThrottlingMiddleware
    {
        private readonly RequestQueue _requestQueue;
        private readonly RequestThrottlingOptions _options;
        private readonly RequestDelegate _next;
        private readonly ILogger _logger;

        /// <summary>
        /// Creates a new <see cref="RequestThrottlingMiddleware"/>.
        /// </summary>
        /// <param name="next">The <see cref="RequestDelegate"/> representing the next middleware in the pipeline.</param>
        /// <param name="loggerFactory">The <see cref="ILoggerFactory"/> used for logging.</param>
        /// <param name="options">The <see cref="RequestThrottlingOptions"/> containing the initialization parameters.</param>
        public RequestThrottlingMiddleware(RequestDelegate next, ILoggerFactory loggerFactory, IOptions<RequestThrottlingOptions> options)
        {
            if (options.Value.MaxConcurrentRequests == null)
            {
                throw new ArgumentException("The value of 'options.MaxConcurrentRequests' must be specified.", nameof(options));
            }

            _next = next;
            _logger = loggerFactory.CreateLogger<RequestThrottlingMiddleware>();
            _options = options.Value;
            _requestQueue = new RequestQueue(_options.MaxConcurrentRequests.Value); 
        }

        /// <summary>
        /// Invokes the logic of the middleware.
        /// </summary>
        /// <param name="context">The <see cref="HttpContext"/>.</param>
        /// <returns>A <see cref="Task"/> that completes when the request leaves.</returns>
        public async Task Invoke(HttpContext context)
        {
            var waitInQueueTask = _requestQueue.EnterQueue();

            try
            {
                var needsToWaitOnQueue = !waitInQueueTask.IsCompletedSuccessfully;
                if (needsToWaitOnQueue)
                {
                    RequestThrottlingLog.RequestEnqueued(_logger, WaitingRequests);
                    await waitInQueueTask;
                    RequestThrottlingLog.RequestDequeued(_logger, WaitingRequests);
                }

                await _next(context);
            }
            finally
            {
                _requestQueue.Release();
            }
        }

        /// <summary>
        /// The number of live requests that are downstream from this middleware.
        /// Cannot exceeed <see cref="RequestThrottlingOptions.MaxConcurrentRequests"/>.
        /// </summary>
        internal int ConcurrentRequests
        {
            get => _requestQueue.ConcurrentRequests; 
        }

        /// <summary>
        /// Number of requests currently enqueued and waiting to be processed.
        /// </summary>
        internal int WaitingRequests
        {
            get => _requestQueue.WaitingRequests;
        }
    }
}
