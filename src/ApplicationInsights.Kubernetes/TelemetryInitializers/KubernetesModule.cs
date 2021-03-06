﻿using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Kubernetes.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using static Microsoft.ApplicationInsights.Kubernetes.StringUtils;

namespace Microsoft.ApplicationInsights.Kubernetes
{
    public class KubernetesModule : ITelemetryModule
    {
        /// <summary>
        /// Initialize KubernetesModule
        /// </summary>
        /// <param name="configuration"></param>
        public void Initialize(TelemetryConfiguration configuration)
        {
            Initialize(configuration, null);
        }

        /// <summary>
        /// Initialize KubernetesModule
        /// </summary>
        /// <param name="configuration">Telemetry configuration.</param>
        /// <param name="loggerFactory">Optional logger factory for self-diagnostics.</param>
        /// <param name="timeout">Timeout for creating the kubernetes environments.</param>
        public static void Initialize(TelemetryConfiguration configuration, TimeSpan? timeout)
        {
            // Temporary fix to make sure that we initialize module once.
            // It should be removed when configuration reading logic is moved to Web SDK.
            if (!isInitialized)
            {
                lock (lockObject)
                {
                    if (!isInitialized)
                    {
                        EnableKubernetes(null, configuration, timeout);
                    }
                }
            }
        }

        /// <summary>
        /// Enable applicaiton insights for kubernetes.
        /// </summary>
        /// <param name="loggerFactory"></param>
        /// <param name="timeout"></param>
        public static void EnableKubernetes(IServiceCollection serviceCollection,
            TelemetryConfiguration configuration,
            TimeSpan? timeout = null,
            IKubernetesServiceCollectionBuilder kubernetesServiceCollectionBuilder = null)
        {
            // 2 minutes maximum to spin up the container.
            timeout = timeout ?? TimeSpan.FromMinutes(2);

            serviceCollection = BuildK8sServiceCollection(serviceCollection, kubernetesServiceCollectionBuilder);
            IServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
            ILogger logger = serviceProvider.GetService<ILogger<KubernetesModule>>();

            Task.Run(() =>
            {
                try
                {
                    string versionInfo = typeof(ApplicationInsightsExtensions).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
                    logger.LogInformation(Invariant($"ApplicationInsights.Kubernetes.Version:{versionInfo}"));
                }
                catch (Exception ex)
                {
                    logger.LogError("Failed to fetch ApplicaitonInsights.Kubernetes' version info. Details" + ex.ToString());
                }
            });

            try
            {
                K8sEnvironment k8sEnv = serviceProvider.GetRequiredService<IK8sEnvironmentFactory>().CreateAsync(timeout.Value).ConfigureAwait(false).GetAwaiter().GetResult();
                if (k8sEnv != null)
                {
                    // Inject the telemetry initializer.
                    ITelemetryInitializer initializer = new KubernetesTelemetryInitializer(k8sEnv,
                        SDKVersionUtils.Instance,
                        serviceProvider.GetService<ILogger<KubernetesTelemetryInitializer>>());
                    configuration.TelemetryInitializers.Add(initializer);
                    logger?.LogDebug("Application Insights Kubernetes injected the service successfully.");
                }
                else
                {
                    logger?.LogError("Application Insights Kubernetes failed to start.");
                }
                isInitialized = true;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex.ToString());
            }
        }

        internal static IServiceCollection BuildK8sServiceCollection(IServiceCollection services, IKubernetesServiceCollectionBuilder kubernetesServiceCollectionBuilder = null)
        {
            kubernetesServiceCollectionBuilder = kubernetesServiceCollectionBuilder ?? new KubernetesServiceCollectionBuilder();
            Services = kubernetesServiceCollectionBuilder.InjectServices(services);
            return Services;
        }

        private static readonly object lockObject = new object();
        private static bool isInitialized = false;
        internal static IServiceCollection Services { get; private set; }
    }
}
