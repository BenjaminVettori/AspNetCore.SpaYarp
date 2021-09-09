﻿using AspNetCore.SpaYarp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using Yarp.ReverseProxy.Forwarder;

namespace Microsoft.AspNetCore.Builder;

public static class WebApplicationExtensions
{
    /// <summary>
    /// Adds the middlewares for the SPA proxy to the pipeline.
    /// The middlewares get only added if the 'spa.proxy.json' file exists and the SpaYarp services were added (there is a check for the SpaProxyLaunchManager).
    /// </summary>
    /// <param name="app">The web application used to configure the HTTP pipeline, and routes.</param>
    /// <returns>The web application.</returns>
    public static WebApplication UseSpaYarp(this WebApplication app)
    {
        var spaProxyLaunchManager = app.Services.GetService<SpaProxyLaunchManager>();

        if (spaProxyLaunchManager == null)
        {
            return app;
        }

        app.UseMiddleware<SpaProxyMiddleware>();

        // configure the proxy
        var forwarder = app.Services.GetRequiredService<IHttpForwarder>();
        var spaOptions = app.Services.GetRequiredService<IOptions<SpaDevelopmentServerOptions>>().Value;

        // Configure our own HttpMessageInvoker for outbound calls for proxy operations
        var httpClient = new HttpMessageInvoker(new SocketsHttpHandler()
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false
        });

        var transformer = new CustomTransformer();
        var requestOptions = new ForwarderRequestConfig { Timeout = TimeSpan.FromSeconds(100) };

        app.Map("/{**catch-all}", async httpContext =>
        {
            var error = await forwarder.SendAsync(httpContext, spaOptions.ClientUrl, httpClient, requestOptions, transformer);
            // Check if the proxy operation was successful
            if (error != ForwarderError.None)
            {
                var errorFeature = httpContext.Features.Get<IForwarderErrorFeature>();
                var exception = errorFeature?.Exception;
            }
        });

        return app;
    }

    /// <summary>
    /// Custom request transformation
    /// </summary>
    private class CustomTransformer : HttpTransformer
    {
        /// <summary>
        /// A callback that is invoked prior to sending the proxied request. All HttpRequestMessage
        /// fields are initialized except RequestUri, which will be initialized after the
        /// callback if no value is provided. The string parameter represents the destination
        /// URI prefix that should be used when constructing the RequestUri. The headers
        /// are copied by the base implementation, excluding some protocol headers like HTTP/2
        /// pseudo headers (":authority").
        /// </summary>
        /// <param name="httpContext">The incoming request.</param>
        /// <param name="proxyRequest">The outgoing proxy request.</param>
        /// <param name="destinationPrefix">The uri prefix for the selected destination server which can be used to create
        /// the RequestUri.</param>
        public override async ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix)
        {
            // Copy all request headers
            await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix);

            // Suppress the original request header, use the one from the destination Uri.
            proxyRequest.Headers.Host = null;
        }
    }
}
