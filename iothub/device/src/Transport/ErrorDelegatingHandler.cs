// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Azure.Devices.Client.Transport
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client.Exceptions;
    using Microsoft.Azure.Devices.Client.Extensions;
    using Microsoft.Azure.Devices.Shared;
    using System.Diagnostics;
    using System.Security.Authentication;
    using System.Runtime.InteropServices;

    sealed class ErrorDelegatingHandler : DefaultDelegatingHandler
    {

        internal static readonly HashSet<Type> TransientExceptions = new HashSet<Type>
        {
            typeof(IotHubClientTransientException),
            typeof(IotHubCommunicationException),
            typeof(ServerBusyException),
            typeof(IOException),
            typeof(TimeoutException),
            typeof(ObjectDisposedException),
            typeof(OperationCanceledException),
            typeof(TaskCanceledException),
            typeof(IotHubThrottledException),
            typeof(System.Net.Sockets.SocketException),
        };

        internal static readonly HashSet<Type> TransportTransientExceptions = new HashSet<Type>
        {
            typeof(IotHubThrottledException),
            typeof(IotHubClientTransientException),
            typeof(ServerBusyException),
            typeof(OperationCanceledException),
            typeof(TaskCanceledException),
        };

        TaskCompletionSource<int> openCompletion;

        public ErrorDelegatingHandler(IPipelineContext context)
            : base(context)
        {
        }

        public override async Task OpenAsync(bool explicitOpen, CancellationToken cancellationToken)
        {
            try
            {
                if (Logging.IsEnabled) Logging.Enter(this, explicitOpen, cancellationToken, $"{nameof(ErrorDelegatingHandler)}.{nameof(OpenAsync)}");

                TaskCompletionSource<int> openCompletionBeforeOperationStarted = Volatile.Read(ref this.openCompletion);
                if (openCompletionBeforeOperationStarted == null)
                {
                    openCompletionBeforeOperationStarted = new TaskCompletionSource<int>();
                    TaskCompletionSource<int> currentOpenPromise;
                    if ((currentOpenPromise = Interlocked.CompareExchange(ref this.openCompletion, openCompletionBeforeOperationStarted, null)) == null)
                    {
                        IDelegatingHandler handlerBeforeOperationStarted = this.ContinuationFactory(Context);
                        this.InnerHandler = handlerBeforeOperationStarted;
                        try
                        {
                            await this.ExecuteWithErrorHandlingAsync(() => base.OpenAsync(explicitOpen, cancellationToken), false, cancellationToken).ConfigureAwait(false);
                            openCompletionBeforeOperationStarted.TrySetResult(0);
                        }
                        catch (Exception ex) when (IsTransportHandlerStillUsable(ex))
                        {
                            await this.Reset(openCompletionBeforeOperationStarted, handlerBeforeOperationStarted).ConfigureAwait(false);
                            throw;
                        }
                        catch (Exception ex) when (!ex.IsFatal())
                        {
                            if (Logging.IsEnabled) Logging.Error(this, ex.Message, $"{nameof(ErrorDelegatingHandler)}.{nameof(OpenAsync)}");
                            throw;
                        }
                    }
                    else
                    {
                        if (Logging.IsEnabled) Logging.Info(this, "Awaiting new Open task.", $"{nameof(ErrorDelegatingHandler)}.{nameof(OpenAsync)}");

                        await currentOpenPromise.Task.ConfigureAwait(false);
                    }
                }
                else
                {
                    if (Logging.IsEnabled) Logging.Info(this, "Awaiting existing Open task.", $"{nameof(ErrorDelegatingHandler)}.{nameof(OpenAsync)}");

                    await openCompletionBeforeOperationStarted.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                if (Logging.IsEnabled) Logging.Exit(this, explicitOpen, cancellationToken, $"{nameof(ErrorDelegatingHandler)}.{nameof(OpenAsync)}");
            }
        }

        public override Task<Message> ReceiveAsync(CancellationToken cancellationToken)
        {
            return this.ExecuteWithErrorHandlingAsync(() => base.ReceiveAsync(cancellationToken), true, cancellationToken);
        }

        public override Task<Message> ReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            return this.ExecuteWithErrorHandlingAsync(() => base.ReceiveAsync(timeout, cancellationToken), true, cancellationToken);
        }

        public override Task EnableMethodsAsync(CancellationToken cancellationToken)
        {
            return this.ExecuteWithErrorHandlingAsync(() => base.EnableMethodsAsync(cancellationToken), true, cancellationToken);
        }

        public override Task DisableMethodsAsync(CancellationToken cancellationToken)
        {
            return this.ExecuteWithErrorHandlingAsync(() => base.DisableMethodsAsync(cancellationToken), true, cancellationToken);
        }

        public override Task EnableEventReceiveAsync(CancellationToken cancellationToken)
        {
            return this.ExecuteWithErrorHandlingAsync(() => base.EnableEventReceiveAsync(cancellationToken), true, cancellationToken);
        }

        public override Task DisableEventReceiveAsync(CancellationToken cancellationToken)
        {
            return this.ExecuteWithErrorHandlingAsync(() => base.DisableEventReceiveAsync(cancellationToken), true, cancellationToken);
        }

        public override Task RecoverConnections(object o, ConnectionType connectionType, CancellationToken cancellationToken)
        {
            return this.ExecuteWithErrorHandlingAsync(() => base.RecoverConnections(o, connectionType, cancellationToken), true, cancellationToken);
        }

        public override Task EnableTwinPatchAsync(CancellationToken cancellationToken)
        {
            return this.ExecuteWithErrorHandlingAsync(() => base.EnableTwinPatchAsync(cancellationToken), true, cancellationToken);
        }
        
        public override Task<Twin> SendTwinGetAsync(CancellationToken cancellationToken)
        {
            return this.ExecuteWithErrorHandlingAsync(() => base.SendTwinGetAsync(cancellationToken), true, cancellationToken);
        }
        
        public override Task SendTwinPatchAsync(TwinCollection reportedProperties,  CancellationToken cancellationToken)
        {
            return this.ExecuteWithErrorHandlingAsync(() => base.SendTwinPatchAsync(reportedProperties, cancellationToken), true, cancellationToken);
        }

        public override Task AbandonAsync(string lockToken, CancellationToken cancellationToken)
        {
            return this.ExecuteWithErrorHandlingAsync(() => base.AbandonAsync(lockToken, cancellationToken), true, cancellationToken);
        }

        public override Task CompleteAsync(string lockToken, CancellationToken cancellationToken)
        {
            return this.ExecuteWithErrorHandlingAsync(() => base.CompleteAsync(lockToken, cancellationToken), true, cancellationToken);
        }

        public override Task RejectAsync(string lockToken, CancellationToken cancellationToken)
        {
            return this.ExecuteWithErrorHandlingAsync(() => base.RejectAsync(lockToken, cancellationToken), true, cancellationToken);
        }

        public override Task SendEventAsync(IEnumerable<Message> messages, CancellationToken cancellationToken)
        {
            return this.ExecuteWithErrorHandlingAsync(() => base.SendEventAsync(messages, cancellationToken), true, cancellationToken);
        }

        public override Task SendEventAsync(Message message, CancellationToken cancellationToken)
        {
            return this.ExecuteWithErrorHandlingAsync(() => base.SendEventAsync(message, cancellationToken), true, cancellationToken);
        }

        public override Task SendMethodResponseAsync(MethodResponseInternal methodResponse, CancellationToken cancellationToken)
        {
            return this.ExecuteWithErrorHandlingAsync(() => base.SendMethodResponseAsync(methodResponse, cancellationToken), true, cancellationToken);
        }

        Task ExecuteWithErrorHandlingAsync(Func<Task> asyncOperation, bool ensureOpen, CancellationToken cancellationToken)
        {
            return ExecuteWithErrorHandlingAsync(async () => { await asyncOperation().ConfigureAwait(false); return 0; }, ensureOpen, cancellationToken);
        }

        async Task<T> ExecuteWithErrorHandlingAsync<T>(Func<Task<T>> asyncOperation, bool ensureOpen, CancellationToken cancellationToken)
        {
            try
            {
                if (Logging.IsEnabled) Logging.Enter(this, ensureOpen, cancellationToken, $"{nameof(ErrorDelegatingHandler)}.{nameof(ExecuteWithErrorHandlingAsync)}");

                if (ensureOpen)
                {
                    await this.EnsureOpenAsync(cancellationToken).ConfigureAwait(false);
                }

                TaskCompletionSource<int> openCompletionBeforeOperationStarted = Volatile.Read(ref this.openCompletion);
                IDelegatingHandler handlerBeforeOperationStarted = this.InnerHandler;

                try
                {
                    return await asyncOperation().ConfigureAwait(false);
                }
                catch (Exception ex) when (!ex.IsFatal())
                {
                    if (IsTransient(ex))
                    {
                        if (IsTransportHandlerStillUsable(ex))
                        {
                            if (Logging.IsEnabled) Logging.Error(this, $"Transient exception caught; IsTransportHandlerStillUsable=true : {ex}");

                            if (ex is IotHubClientTransientException)
                            {
                                throw;
                            }

                            throw new IotHubClientTransientException("Transient error occurred, please retry.", ex);
                        }

                        await this.Reset(openCompletionBeforeOperationStarted, handlerBeforeOperationStarted).ConfigureAwait(false);

                        if (Logging.IsEnabled) Logging.Error(this, $"Transient exception caught; IsTransportHandlerStillUsable=false : {ex}");

                        if (ex is IotHubClientTransientException)
                        {
                            throw;
                        }
                        throw new IotHubClientTransientException("Transient error occurred, please retry.", ex);
                    }
                    else
                    {
                        if (Logging.IsEnabled) Logging.Error(this, $"Non-transient exception caught: {ex}");

                        await this.Reset(openCompletionBeforeOperationStarted, handlerBeforeOperationStarted).ConfigureAwait(false);
                        throw;
                    }
                }
            }
            finally
            {
                if (Logging.IsEnabled) Logging.Exit(this, ensureOpen, cancellationToken, $"{nameof(ErrorDelegatingHandler)}.{nameof(ExecuteWithErrorHandlingAsync)}");
            }
        }

        Task EnsureOpenAsync(CancellationToken cancellationToken)
        {
            return this.OpenAsync(false, cancellationToken);
        }

        static bool IsTransportHandlerStillUsable(Exception exception)
        {
            return exception.Unwind(true).Any(e => TransportTransientExceptions.Contains(e.GetType()));
        }

        static bool IsTransient(Exception exception)
        {
            return exception.Unwind(true).Any(e => TransientExceptions.Contains(e.GetType())) && 
                   exception.Unwind(true).All(e => !IsSecurity(e));
        }

        static bool IsSecurity(Exception exception)
        {
            if (// WinHttpException (0x80072F8F): A security error occurred.
                (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && (exception.HResult == unchecked((int)0x80072F8F))) ||
                // CURLE_SSL_CACERT (60): Peer certificate cannot be authenticated with known CA certificates.
                (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && (exception.HResult == 60)) ||
                exception is AuthenticationException)
            {
                return true;
            }

            return false;
        }
        
        async Task Reset(TaskCompletionSource<int> openCompletionBeforeOperationStarted, IDelegatingHandler handlerBeforeOperationStarted)
        {

            try
            {
                if (Logging.IsEnabled) Logging.Enter(this, openCompletionBeforeOperationStarted, handlerBeforeOperationStarted, $"{nameof(ErrorDelegatingHandler)}.{nameof(Reset)}");

                if (openCompletionBeforeOperationStarted == Volatile.Read(ref this.openCompletion))
                {
                    if (Interlocked.CompareExchange(ref this.openCompletion, null, openCompletionBeforeOperationStarted) == openCompletionBeforeOperationStarted)
                    {
                        await Cleanup(handlerBeforeOperationStarted).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                if (Logging.IsEnabled) Logging.Exit(this, $"{nameof(ErrorDelegatingHandler)}.{nameof(Reset)}");
            }
        }

        static async Task Cleanup(IDelegatingHandler handler)
        {
            try
            {
                if (handler != null)
                {
                    await handler.CloseAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                if (Logging.IsEnabled) Logging.Error(handler, $"Unexpected exception: {ex}", $"{nameof(ErrorDelegatingHandler)}.{nameof(Cleanup)}");
            }
        }
    }
}
