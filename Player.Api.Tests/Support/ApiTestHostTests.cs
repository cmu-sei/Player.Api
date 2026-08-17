// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Player.Api.Tests.Support;

/// <summary>
/// Guards <see cref="ApiTestHost"/> itself.
/// </summary>
public class ApiTestHostTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    /// <summary>
    /// Constructs every registered request handler and notification handler.
    /// </summary>
    /// <remarks>
    /// A dependency added to a handler in production is otherwise found by whichever test happens to
    /// send that request, as a <c>Unable to resolve service for type ...</c> buried in an unrelated
    /// failure. This turns it into one failure that names the handler and the missing service, and it
    /// covers the handlers no test sends a request to yet.
    /// </remarks>
    [Fact]
    public void Every_handler_the_application_registers_can_be_constructed()
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Startup>());

        var handlerTypes = services
            .Where(x => x.ServiceType.IsGenericType)
            .Where(x => x.ServiceType.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
                || x.ServiceType.GetGenericTypeDefinition() == typeof(IRequestHandler<>)
                || x.ServiceType.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
            .Select(x => x.ServiceType)
            .Distinct()
            .ToList();

        // A registration scan that found nothing would make this pass while asserting nothing.
        Assert.NotEmpty(handlerTypes);

        var failures = new List<string>();

        foreach (var handlerType in handlerTypes)
        {
            try
            {
                // GetServices, not GetService: MediatR registers notification handlers as a
                // collection, and resolving the collection is what constructs each one.
                RootHost.Resolve<IServiceProvider>().GetServices(handlerType).ToList();
            }
            catch (Exception ex)
            {
                failures.Add($"{handlerType.GenericTypeArguments[0].FullName}: {ex.Message}");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {handlerTypes.Count} handler registration(s) could not be " +
                $"constructed:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    /// <summary>
    /// The host must act as the principal it was given, since every authorization decision and every
    /// "current user" lookup in the application reads back through this seam.
    /// </summary>
    [Fact]
    public void The_resolved_identity_is_the_principal_the_host_was_built_with()
    {
        var builder = new ClaimsPrincipalBuilder();
        var host = HostFor(builder.Build());

        Assert.Equal(builder.UserId, host.Resolve<Player.Api.Infrastructure.Authorization.IIdentityResolver>().GetId());
        Assert.True(host.Resolve<Player.Api.Infrastructure.Authorization.IPlayerAuthorizationService>()
            .IsCurrentUser(builder.UserId));
    }

    /// <summary>
    /// One host per principal is what lets a test resolve a service more than once and get the same
    /// container, so a service that reads its own earlier writes sees them.
    /// </summary>
    [Fact]
    public void A_principal_gets_one_host_however_many_times_it_is_asked_for()
    {
        var user = new ClaimsPrincipalBuilder().Build();

        Assert.Same(HostFor(user), HostFor(user));
    }

    /// <summary>
    /// Options are applied when the host is built, so a second set for an already-hosted principal cannot
    /// take effect.
    /// </summary>
    /// <remarks>
    /// Returning the first host silently is the failure this guards: the test would then assert against a
    /// configuration it did not set, and pass or fail for a reason written nowhere in it — the worst kind,
    /// since the arrange step reads as though it is in force. Asserting the message too, because the way out
    /// (build a principal per configuration) is not guessable from the exception type alone.
    /// </remarks>
    [Fact]
    public void Configuring_a_principal_that_already_has_a_host_throws()
    {
        var user = new ClaimsPrincipalBuilder().Build();
        HostFor(user, options => options.FileUpload.maxSize = 16);

        var ex = Assert.Throws<InvalidOperationException>(
            () => HostFor(user, options => options.FileUpload.maxSize = 32));

        Assert.Contains("already been built", ex.Message);
    }
}
