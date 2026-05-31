// Marker interfaces — used to declare intent at the type level.
// Queries never mutate state; Commands never return data.
// Java parallel: CQRS marker annotations in Axon / Spring; Mediatr in .NET.

namespace Lesson.Cqrs;

/// <summary>
/// Marker interface for a command that returns a result of type <typeparamref name="TResult"/>.
/// Commands change state and may return a result (e.g. the created resource id).
/// </summary>
public interface ICommand<out TResult> : MediatR.IRequest<TResult> { }

/// <summary>
/// Marker interface for a query that returns a result of type <typeparamref name="TResult"/>.
/// Queries are side-effect free.
/// </summary>
public interface IQuery<out TResult> : MediatR.IRequest<TResult> { }
