// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;

namespace Player.Api.Tests.Support;

/// <summary>
/// Answers outbound requests from a fixed table, for handlers that fetch over HTTP.
/// </summary>
/// <remarks>
/// The alternative is substituting <see cref="IHttpClientFactory"/> to return a substituted client,
/// which cannot be done — <see cref="HttpClient"/> is a concrete class with no virtual
/// <c>GetAsync</c>. Replacing the message handler is the supported seam.
/// </remarks>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, byte[] Content, string ContentType)> _responses = [];
    private readonly Dictionary<string, Exception> _failures = [];
    private readonly Lock _recording = new();

    private readonly List<string> _requests = [];
    private readonly List<SentRequest> _sent = [];

    /// <summary>Every absolute uri that was requested, in order.</summary>
    /// <remarks>
    /// A snapshot taken under the recording lock rather than the live list. The callers under test include
    /// background senders that record from their own threads, and reading a <c>List&lt;T&gt;</c> while
    /// another thread adds to it is undefined — which a test that polls this in a loop will eventually
    /// find out.
    /// </remarks>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_recording)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>
    /// The same requests with their method, headers and body, for the callers whose behaviour is in what
    /// they send rather than what they do with the answer. Also a snapshot.
    /// </summary>
    public IReadOnlyList<SentRequest> Sent
    {
        get
        {
            lock (_recording)
            {
                return [.. _sent];
            }
        }
    }

    public StubHttpMessageHandler Respond(
        string uri,
        byte[] content,
        string contentType = "image/png",
        HttpStatusCode status = HttpStatusCode.OK)
    {
        _responses[uri] = (status, content, contentType);
        return this;
    }

    public StubHttpMessageHandler RespondWithStatus(string uri, HttpStatusCode status)
    {
        _responses[uri] = (status, [], null);
        return this;
    }

    /// <summary>Fails the request, for the paths that handle a transport error rather than a status.</summary>
    public StubHttpMessageHandler RespondByThrowing(string uri, Exception exception)
    {
        _failures[uri] = exception;
        return this;
    }

    /// <summary>An unregistered uri answers 404, so a test only lists what it cares about.</summary>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri.ToString();
        var captured = await Capture(request, cancellationToken);

        // Locked because callers include background senders that run a request per queue in parallel, and
        // because Requests and Sent snapshot under the same lock.
        lock (_recording)
        {
            _requests.Add(uri);
            _sent.Add(captured);
        }

        if (_failures.TryGetValue(uri, out var failure))
        {
            throw failure;
        }

        if (!_responses.TryGetValue(uri, out var stubbed))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var response = new HttpResponseMessage(stubbed.Status)
        {
            Content = new ByteArrayContent(stubbed.Content)
        };

        if (stubbed.ContentType != null)
        {
            response.Content.Headers.ContentType = new(stubbed.ContentType);
        }

        return response;
    }

    /// <summary>
    /// Copies the request, because the caller disposes it — and its content — as soon as the response is
    /// read, leaving nothing to assert on afterwards.
    /// </summary>
    private static async Task<SentRequest> Capture(HttpRequestMessage request, CancellationToken ct)
    {
        var headers = request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value));

        return new SentRequest(
            request.RequestUri.ToString(),
            request.Method,
            request.Content == null ? null : await request.Content.ReadAsStringAsync(ct),
            request.Content?.Headers.ContentType?.MediaType,
            headers);
    }
}

/// <summary>One outbound request as it left, headers and body included.</summary>
public sealed record SentRequest(
    string Uri,
    HttpMethod Method,
    string Body,
    string ContentType,
    IReadOnlyDictionary<string, string> Headers);
