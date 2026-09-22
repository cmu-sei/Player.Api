// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Player.Api.Infrastructure.Filters;

namespace Player.Api.Tests.Infrastructure.Filters;

/// <summary>
/// Turns model binding failures into the 400 a client sees. It short-circuits the action by setting a result,
/// which is why a handler can assume its request object is already valid.
/// </summary>
public class ValidateModelStateFilterTests
{
    [Fact]
    public void A_valid_request_reaches_the_action()
    {
        Assert.Null(Execute().Result);
    }

    [Fact]
    public void An_invalid_request_is_a_400_titled_Invalid_Data()
    {
        var problem = Problem(Execute(("name", "The Name field is required.")));

        Assert.Equal(400, problem.Status);
        Assert.Equal("Invalid Data", problem.Title);
    }

    /// <summary>
    /// Every failure is reported, one per line, each prefixed with the field it came from — a client sending
    /// several bad fields does not have to fix them one round trip at a time.
    /// </summary>
    [Fact]
    public void Every_failing_field_is_listed()
    {
        var problem = Problem(Execute(
            ("name", "The Name field is required."),
            ("viewId", "The value 'nope' is not valid.")));

        Assert.Equal(
            "name: The Name field is required.\nviewId: The value 'nope' is not valid.",
            problem.Detail);
    }

    [Fact]
    public void Several_failures_on_one_field_are_all_listed()
    {
        var problem = Problem(Execute(("name", "Required."), ("name", "Too long.")));

        Assert.Equal("name: Required.\nname: Too long.", problem.Detail);
    }

    /// <summary>
    /// A binding failure can carry an exception instead of a message — a value provider that threw, say.
    /// Falling back to the exception's message keeps the field from being reported with an empty reason.
    /// </summary>
    [Fact]
    public void A_failure_with_no_message_falls_back_to_its_exception()
    {
        var context = Context();
        context.ModelState.AddModelError(
            "viewId",
            new InvalidOperationException("The value provider failed."),
            new EmptyModelMetadataProvider().GetMetadataForType(typeof(Guid)));

        new ValidateModelStateFilter().OnActionExecuting(context);

        Assert.Equal("viewId: The value provider failed.", Problem(context).Detail);
    }

    private static ActionExecutingContext Execute(params (string Field, string Message)[] errors)
    {
        var context = Context();

        foreach (var (field, message) in errors)
        {
            context.ModelState.AddModelError(field, message);
        }

        new ValidateModelStateFilter().OnActionExecuting(context);
        return context;
    }

    private static ActionExecutingContext Context() =>
        new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            [], new Dictionary<string, object>(), controller: null);

    private static ProblemDetails Problem(ActionExecutingContext context) =>
        Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(context.Result).Value);
}
