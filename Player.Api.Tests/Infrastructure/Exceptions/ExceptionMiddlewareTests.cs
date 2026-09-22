// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Infrastructure.Exceptions.Middleware;

namespace Player.Api.Tests.Infrastructure.Exceptions;

/// <summary>
/// The outermost error handler: every exception that escapes a request becomes this response. It decides the
/// status code and how much of the exception the caller is shown, so the development/production split here is
/// the line between a useful stack trace and leaking internals.
/// </summary>
public class ExceptionMiddlewareTests
{
    [Fact]
    public async Task A_request_that_does_not_throw_is_left_alone()
    {
        var context = NewContext();
        var handled = false;

        await Middleware(_ => { handled = true; return Task.CompletedTask; }).InvokeAsync(context);

        Assert.True(handled);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Null(context.Response.ContentType);
    }

    /// <summary>
    /// The mapped statuses come from <see cref="IApiException"/>; anything else is a 500. This is what turns a
    /// handler's <c>throw new ForbiddenException()</c> into a 403 without the handler touching the response.
    /// </summary>
    [Theory]
    [InlineData(typeof(ConflictException), (int)HttpStatusCode.Conflict)]
    [InlineData(typeof(ForbiddenException), (int)HttpStatusCode.Forbidden)]
    [InlineData(typeof(EntityNotFoundException<ViewEntity>), (int)HttpStatusCode.NotFound)]
    [InlineData(typeof(InvalidOperationException), (int)HttpStatusCode.InternalServerError)]
    public async Task The_status_comes_from_the_exception_type(Type exceptionType, int expected)
    {
        var context = NewContext();

        await Throw(context, (Exception)Activator.CreateInstance(exceptionType));

        Assert.Equal(expected, context.Response.StatusCode);
        Assert.Equal(expected, (await Body(context)).Status);
    }

    [Fact]
    public async Task The_response_is_problem_json()
    {
        var context = NewContext();

        await Throw(context, new ConflictException());

        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    /// <summary>
    /// A mapped exception's message is the whole of what the caller sees — these are messages handlers wrote
    /// deliberately, so no detail is added and none is withheld.
    /// </summary>
    [Fact]
    public async Task A_mapped_exception_reports_its_own_message()
    {
        var context = NewContext();

        await Throw(context, new ConflictException("That name is already in use."));

        var body = await Body(context);
        Assert.Equal("That name is already in use.", body.Title);
        Assert.Null(body.Detail);
    }

    /// <summary>
    /// An unmapped exception is a bug, and in development the full <c>ToString()</c> — stack trace included —
    /// is returned so it can be read without going to the logs.
    /// </summary>
    [Fact]
    public async Task In_development_a_server_error_returns_the_whole_exception()
    {
        var context = NewContext();
        var thrown = Caught(new InvalidOperationException("view has no primary team"));

        await Throw(context, thrown, "Development");

        var body = await Body(context);
        Assert.Equal("view has no primary team", body.Title);
        Assert.Equal(thrown.ToString(), body.Detail);
        Assert.Contains(nameof(Caught), body.Detail);
    }

    /// <summary>
    /// Outside development the title is generic. Note the message itself still ships as the detail, so an
    /// exception message is reachable by any caller and must not carry anything sensitive.
    /// </summary>
    [Fact]
    public async Task Outside_development_a_server_error_hides_the_stack_trace()
    {
        var context = NewContext();

        await Throw(context, Caught(new InvalidOperationException("view has no primary team")), "Production");

        var body = await Body(context);
        Assert.Equal("A server error occurred.", body.Title);
        Assert.Equal("view has no primary team", body.Detail);
    }

    private ExceptionMiddleware Middleware(RequestDelegate next, string environment = "Production")
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(environment);

        // The factory is a constructor dependency the middleware only uses in commented-out code.
        return new ExceptionMiddleware(
            next, NullLogger<ExceptionMiddleware>.Instance, env, Substitute.For<ProblemDetailsFactory>());
    }

    private Task Throw(HttpContext context, Exception exception, string environment = "Production") =>
        Middleware(_ => Task.FromException(exception), environment).InvokeAsync(context);

    private static DefaultHttpContext NewContext() =>
        new() { Response = { Body = new MemoryStream() } };

    private static async Task<ProblemDetails> Body(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonSerializer.DeserializeAsync<ProblemDetails>(
            context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Gives the exception a stack trace, which an unthrown one does not have.</summary>
    private static Exception Caught(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }
}
