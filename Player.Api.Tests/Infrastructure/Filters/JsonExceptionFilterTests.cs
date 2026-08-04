// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Infrastructure.Filters;

namespace Player.Api.Tests.Infrastructure.Filters;

/// <summary>
/// The MVC-level counterpart to <see cref="Exceptions.ExceptionMiddlewareTests"/>: same status mapping and the
/// same development split, but it produces a result rather than writing the response, and it leaves
/// <c>ExceptionHandled</c> alone — so the middleware sees the exception too.
/// </summary>
public class JsonExceptionFilterTests
{
    [Theory]
    [InlineData(typeof(ConflictException), (int)HttpStatusCode.Conflict)]
    [InlineData(typeof(ForbiddenException), (int)HttpStatusCode.Forbidden)]
    [InlineData(typeof(EntityNotFoundException<ViewEntity>), (int)HttpStatusCode.NotFound)]
    [InlineData(typeof(InvalidOperationException), (int)HttpStatusCode.InternalServerError)]
    public void The_status_comes_from_the_exception_type(Type exceptionType, int expected)
    {
        var context = Handle((Exception)Activator.CreateInstance(exceptionType));

        var result = Assert.IsType<JsonResult>(context.Result);
        Assert.Equal(expected, result.StatusCode);
        Assert.Equal(expected, Assert.IsType<ProblemDetails>(result.Value).Status);
    }

    [Fact]
    public void A_mapped_exception_reports_its_own_message()
    {
        var problem = Problem(Handle(new ForbiddenException("Not a member of that view.")));

        Assert.Equal("Not a member of that view.", problem.Title);
        Assert.Null(problem.Detail);
    }

    /// <summary>
    /// Only the stack trace, unlike the middleware, which returns the full <c>ToString()</c>. A client seeing
    /// both formats for the same class of failure is expected.
    /// </summary>
    [Fact]
    public void In_development_a_server_error_returns_the_stack_trace()
    {
        var thrown = Caught(new InvalidOperationException("no primary team"));

        var problem = Problem(Handle(thrown, "Development"));

        Assert.Equal("no primary team", problem.Title);
        Assert.Equal(thrown.StackTrace, problem.Detail);
    }

    [Fact]
    public void Outside_development_a_server_error_hides_the_stack_trace()
    {
        var problem = Problem(Handle(Caught(new InvalidOperationException("no primary team"))));

        Assert.Equal("A server error occurred.", problem.Title);
        Assert.Equal("no primary team", problem.Detail);
    }

    /// <summary>
    /// The filter does not mark the exception handled, so it keeps propagating and
    /// <see cref="Player.Api.Infrastructure.Exceptions.Middleware.ExceptionMiddleware"/> writes the response
    /// that the caller actually receives.
    /// </summary>
    [Fact]
    public void The_exception_is_not_marked_handled()
    {
        Assert.False(Handle(new ConflictException()).ExceptionHandled);
    }

    private static ExceptionContext Handle(Exception exception, string environment = "Production")
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(environment);

        var context = new ExceptionContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [])
        {
            Exception = exception
        };

        new JsonExceptionFilter(env).OnException(context);
        return context;
    }

    private static ProblemDetails Problem(ExceptionContext context) =>
        Assert.IsType<ProblemDetails>(Assert.IsType<JsonResult>(context.Result).Value);

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
