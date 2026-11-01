using System;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Operations
{
    public sealed record OperationProgress(string Stage, string Message, long? Completed = null, long? Total = null);

    public enum OperationStatus
    {
        Succeeded,
        Cancelled,
        Failed
    }

    public sealed record OperationFailure(string Operation, string Message, Exception Cause);

    public sealed class OperationResult<T>
    {
        private readonly T? value;

        private OperationResult(OperationStatus status, T? value, OperationFailure? failure)
        {
            Status = status;
            this.value = value;
            Failure = failure;
        }

        public OperationStatus Status { get; }
        public OperationFailure? Failure { get; }
        public T Value => Status == OperationStatus.Succeeded ? value! :
            throw new InvalidOperationException("Only a successful operation has a result.");

        internal static OperationResult<T> Success(T value) => new(OperationStatus.Succeeded, value, null);
        internal static OperationResult<T> Cancelled() => new(OperationStatus.Cancelled, default, null);
        internal static OperationResult<T> Failed(OperationFailure failure) => new(OperationStatus.Failed, default, failure);
    }

    /// <summary>One context per operation; nested phases share it. Commit is reserved for the final
    /// atomic user-visible change, not an intermediate cache write.</summary>
    public sealed class OperationContext
    {
        private readonly IProgress<OperationProgress>? progress;
        private int commitState;

        public OperationContext(CancellationToken cancellationToken = default, IProgress<OperationProgress>? progress = null)
        {
            CancellationToken = cancellationToken;
            this.progress = progress;
        }

        public CancellationToken CancellationToken { get; }
        public bool IsCancellationRequested => CancellationToken.IsCancellationRequested;
        public bool HasCommitted => Volatile.Read(ref commitState) == 2;

        public void ThrowIfCancellationRequested()
        {
            if (!HasCommitted) CancellationToken.ThrowIfCancellationRequested();
        }

        public void Report(string stage, string message, long? completed = null, long? total = null)
        {
            ThrowIfCancellationRequested();
            progress?.Report(new OperationProgress(stage, message, completed, total));
            ThrowIfCancellationRequested();
        }

        public Action<string> Messages(string stage) => message => Report(stage, message);

        /// <summary>The caller prepares and validates everything before this final atomic change.
        /// Cancellation observed before it prevents publication; successful publication wins a late cancel.</summary>
        public void Commit(Action publish)
        {
            ArgumentNullException.ThrowIfNull(publish);
            ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref commitState, 1, 0) != 0)
                throw new InvalidOperationException("An operation has only one final commit.");
            try
            {
                ThrowIfCancellationRequested();
                publish();
                Volatile.Write(ref commitState, 2);
            }
            catch
            {
                Volatile.Write(ref commitState, 0);
                throw;
            }
        }
    }

    public static class CoreOperation
    {
        public static OperationResult<T> Run<T>(string operation, Func<OperationContext, T> work,
            CancellationToken cancellationToken = default, IProgress<OperationProgress>? progress = null)
        {
            ArgumentNullException.ThrowIfNull(work);
            var context = new OperationContext(cancellationToken, progress);
            try
            {
                context.ThrowIfCancellationRequested();
                T value = work(context);
                context.ThrowIfCancellationRequested();
                return OperationResult<T>.Success(value);
            }
            catch (Exception ex)
            {
                return FromException<T>(operation, context, ex);
            }
        }

        public static async Task<OperationResult<T>> RunAsync<T>(string operation, Func<OperationContext, Task<T>> work,
            CancellationToken cancellationToken = default, IProgress<OperationProgress>? progress = null)
        {
            ArgumentNullException.ThrowIfNull(work);
            var context = new OperationContext(cancellationToken, progress);
            try
            {
                context.ThrowIfCancellationRequested();
                T value = await work(context).ConfigureAwait(false);
                context.ThrowIfCancellationRequested();
                return OperationResult<T>.Success(value);
            }
            catch (Exception ex)
            {
                return FromException<T>(operation, context, ex);
            }
        }

        private static OperationResult<T> FromException<T>(string operation, OperationContext context, Exception exception)
        {
            if (exception is OperationCanceledException && context.IsCancellationRequested && !context.HasCommitted)
                return OperationResult<T>.Cancelled();
            return OperationResult<T>.Failed(new OperationFailure(operation, exception.Message, exception));
        }
    }
}
