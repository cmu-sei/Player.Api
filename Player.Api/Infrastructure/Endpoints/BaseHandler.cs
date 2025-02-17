// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using MediatR;
using System.Threading.Tasks;
using System.Threading;
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Infrastructure.Endpoints;

public abstract class BaseHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken)
    {
        if (!await this.Authorize(request, cancellationToken))
            throw new ForbiddenException();

        return await this.HandleRequest(request, cancellationToken);
    }

    public abstract Task<bool> Authorize(TRequest request, CancellationToken cancellationToken);
    public abstract Task<TResponse> HandleRequest(TRequest request, CancellationToken cancellationToken);
}

public abstract class BaseHandler<TRequest> : IRequestHandler<TRequest>
    where TRequest : IRequest
{
    public async Task Handle(TRequest request, CancellationToken cancellationToken)
    {
        if (!await this.Authorize(request, cancellationToken))
            throw new ForbiddenException();

        await this.HandleRequest(request, cancellationToken);
    }

    public abstract Task<bool> Authorize(TRequest request, CancellationToken cancellationToken);
    public abstract Task HandleRequest(TRequest request, CancellationToken cancellationToken);
}