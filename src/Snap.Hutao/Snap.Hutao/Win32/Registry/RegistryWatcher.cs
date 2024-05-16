﻿// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Win32.Foundation;
using System.Runtime.InteropServices;
using static Snap.Hutao.Win32.AdvApi32;
using static Snap.Hutao.Win32.Macros;

namespace Snap.Hutao.Win32.Registry;

internal sealed partial class RegistryWatcher : IDisposable
{
    private const REG_SAM_FLAGS RegSamFlags =
        REG_SAM_FLAGS.KEY_QUERY_VALUE |
        REG_SAM_FLAGS.KEY_NOTIFY |
        REG_SAM_FLAGS.KEY_READ;

    private const REG_NOTIFY_FILTER RegNotifyFilters =
        REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_NAME |
        REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_ATTRIBUTES |
        REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_LAST_SET |
        REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_SECURITY;

    private readonly ManualResetEvent disposeEvent = new(false);
    private readonly CancellationTokenSource cancellationTokenSource = new();

    private readonly HKEY hKey;
    private readonly string subKey = default!;
    private readonly Action valueChangedCallback;
    private readonly object syncRoot = new();
    private bool disposed;

    public RegistryWatcher(string keyName, Action valueChangedCallback)
    {
        string[] pathArray = keyName.Split('\\');

        hKey = pathArray[0] switch
        {
            nameof(HKEY.HKEY_CLASSES_ROOT) => HKEY.HKEY_CLASSES_ROOT,
            nameof(HKEY.HKEY_CURRENT_USER) => HKEY.HKEY_CURRENT_USER,
            nameof(HKEY.HKEY_LOCAL_MACHINE) => HKEY.HKEY_LOCAL_MACHINE,
            nameof(HKEY.HKEY_USERS) => HKEY.HKEY_USERS,
            nameof(HKEY.HKEY_CURRENT_CONFIG) => HKEY.HKEY_CURRENT_CONFIG,
            _ => throw new ArgumentException($"The registry hive '{pathArray[0]}' is not supported", nameof(keyName)),
        };

        subKey = string.Join("\\", pathArray[1..]);
        this.valueChangedCallback = valueChangedCallback;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        WatchAsync(cancellationTokenSource.Token).SafeForget();
    }

    public void Dispose()
    {
        // Standard no-reentrancy pattern
        if (disposed)
        {
            return;
        }

        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            // Signal the inner while loop to exit
            disposeEvent.Reset();

            // Cancel the outer while loop
            cancellationTokenSource.Cancel();

            // Wait for both loops to exit
            disposeEvent.WaitOne();
            disposeEvent.Dispose();

            cancellationTokenSource.Dispose();
            disposed = true;

            GC.SuppressFinalize(this);
        }
    }

    private async ValueTask WatchAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

                HRESULT hResult = HRESULT_FROM_WIN32(RegOpenKeyExW(hKey, subKey, 0, RegSamFlags, out HKEY registryKey));
                Marshal.ThrowExceptionForHR(hResult);

                using (ManualResetEvent notifyEvent = new(false))
                {
                    HANDLE hEvent = (HANDLE)notifyEvent.SafeWaitHandle.DangerousGetHandle();

                    try
                    {
                        // If disposeEvent is signaled, the Dispose method
                        // has been called and the object is shutting down.
                        // The outer token has already canceled, so we can
                        // skip both loops and exit the method.
                        while (!disposeEvent.WaitOne(0, true))
                        {
                            HRESULT hRESULT = HRESULT_FROM_WIN32(RegNotifyChangeKeyValue(registryKey, true, RegNotifyFilters, hEvent, true));
                            Marshal.ThrowExceptionForHR(hRESULT);

                            if (WaitHandle.WaitAny([notifyEvent, disposeEvent]) is 0)
                            {
                                valueChangedCallback();
                                notifyEvent.Reset();
                            }
                        }
                    }
                    finally
                    {
                        RegCloseKey(registryKey);
                    }
                }
            }

            if (!disposed)
            {
                try
                {
                    // Before exiting, signal the Dispose method.
                    disposeEvent.Set();
                }
                catch
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}