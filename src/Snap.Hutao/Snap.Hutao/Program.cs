﻿// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Snap.Hutao.Core.Logging;
using Snap.Hutao.Core.Threading;
using System.Runtime.InteropServices;
using Windows.UI.ViewManagement;
using WinRT;

namespace Snap.Hutao;

/// <summary>
/// Program class
/// </summary>
public static class Program
{
    private static volatile DispatcherQueue? dispatcherQueue;

    /// <summary>
    /// 异步切换到主线程
    /// </summary>
    /// <returns>等待体</returns>
    public static DispatherQueueSwitchOperation SwitchToMainThreadAsync()
    {
        return new(dispatcherQueue!);
    }

    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [STAThread]
    private static void Main(string[] args)
    {
        _ = args;
        XamlCheckProcessRequirements();
        ComWrappersSupport.InitializeComWrappers();

        InitializeDependencyInjection();

        // In a Desktop app this runs a message pump internally,
        // and does not return until the application shuts down.
        Application.Start(InitializeApp);
    }

    private static void InitializeApp(ApplicationInitializationCallbackParams param)
    {
        dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        DispatcherQueueSynchronizationContext context = new(dispatcherQueue);
        SynchronizationContext.SetSynchronizationContext(context);

        _ = Ioc.Default.GetRequiredService<App>();
    }

    private static void InitializeDependencyInjection()
    {
        IServiceProvider services = new ServiceCollection()

            // Microsoft extension
            .AddLogging(builder => builder
                .AddDebug()
                .AddDatabase())
            .AddMemoryCache()

            // Hutao extensions
            .AddJsonSerializerOptions()
            .AddDatebase()
            .AddInjections()
            .AddHttpClients()

            // Discrete services
            .AddSingleton<IMessenger>(WeakReferenceMessenger.Default)
            .AddSingleton(new UISettings())

            .BuildServiceProvider();

        Ioc.Default.ConfigureServices(services);
    }
}