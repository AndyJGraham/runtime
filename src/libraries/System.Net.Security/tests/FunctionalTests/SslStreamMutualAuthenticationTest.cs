﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading.Tasks;
using System.Net.Test.Common;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

using Xunit;
using System.Runtime.InteropServices;

namespace System.Net.Security.Tests
{
    using Configuration = System.Net.Test.Common.Configuration;

    public class SslStreamMutualAuthenticationTest : IDisposable
    {
        private readonly X509Certificate2 _clientCertificate;
        private readonly X509Certificate2 _serverCertificate;
        private readonly X509Certificate2 _selfSignedCertificate;

        public SslStreamMutualAuthenticationTest()
        {
            _serverCertificate = Configuration.Certificates.GetServerCertificate();
            _clientCertificate = Configuration.Certificates.GetClientCertificate();
            _selfSignedCertificate = Configuration.Certificates.GetSelfSignedServerCertificate();
        }

        public void Dispose()
        {
            _serverCertificate.Dispose();
            _clientCertificate.Dispose();
        }

        public enum ClientCertSource
        {
            ClientCertificate,
            SelectionCallback,
            CertificateContext
        }

        public static TheoryData<bool, ClientCertSource> BoolAndCertSourceData()
        {
            TheoryData<bool, ClientCertSource> data = new();

            foreach (var source in Enum.GetValues<ClientCertSource>())
            {
                data.Add(true, source);
                data.Add(false, source);
            }

            return data;
        }

        [ConditionalTheory(typeof(PlatformDetection), nameof(PlatformDetection.IsNotWindows7))]
        [MemberData(nameof(BoolAndCertSourceData))]
        public async Task SslStream_RequireClientCert_IsMutuallyAuthenticated_ReturnsTrue(bool clientCertificateRequired, ClientCertSource certSource)
        {
            (Stream stream1, Stream stream2) = TestHelper.GetConnectedStreams();
            using (var client = new SslStream(stream1, false, AllowAnyCertificate))
            using (var server = new SslStream(stream2, false, AllowAnyCertificate))
            {
                var clientOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = Guid.NewGuid().ToString("N")
                };

                switch (certSource)
                {
                    case ClientCertSource.ClientCertificate:
                        clientOptions.ClientCertificates = new X509CertificateCollection() { _clientCertificate };
                        break;
                    case ClientCertSource.SelectionCallback:
                        clientOptions.LocalCertificateSelectionCallback = ClientCertSelectionCallback;
                        break;
                    case ClientCertSource.CertificateContext:
                        clientOptions.ClientCertificateContext = SslStreamCertificateContext.Create(_clientCertificate, new());
                        break;
                }

                Task t2 = client.AuthenticateAsClientAsync(clientOptions);
                Task t1 = server.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _serverCertificate,
                    ClientCertificateRequired = clientCertificateRequired
                });

                await TestConfiguration.WhenAllOrAnyFailedWithTimeout(t1, t2);

                if (clientCertificateRequired)
                {
                    Assert.True(client.IsMutuallyAuthenticated, "client.IsMutuallyAuthenticated");
                    Assert.True(server.IsMutuallyAuthenticated, "server.IsMutuallyAuthenticated");
                }
                else
                {
                    // Even though the certificate was provided, it was not requested by the server and thus the client
                    // was not authenticated.
                    Assert.False(client.IsMutuallyAuthenticated, "client.IsMutuallyAuthenticated");
                    Assert.False(server.IsMutuallyAuthenticated, "server.IsMutuallyAuthenticated");
                }
            }
        }

        [ConditionalTheory(typeof(PlatformDetection), nameof(PlatformDetection.IsNotWindows7))]
        [ClassData(typeof(SslProtocolSupport.SupportedSslProtocolsTestData))]
        public async Task SslStream_CachedCredentials_IsMutuallyAuthenticatedCorrect(
           SslProtocols protocol)
        {
            var clientOptions = new SslClientAuthenticationOptions
            {
                ClientCertificates = new X509CertificateCollection() { _clientCertificate },
                EnabledSslProtocols = protocol,
                RemoteCertificateValidationCallback = delegate { return true; },
                TargetHost = Guid.NewGuid().ToString("N")
            };

            for (int i = 0; i < 5; i++)
            {
                (SslStream client, SslStream server) = TestHelper.GetConnectedSslStreams();
                using (client)
                using (server)
                {
                    bool expectMutualAuthentication = (i % 2) == 0;

                    var serverOptions = new SslServerAuthenticationOptions
                    {
                        ClientCertificateRequired = expectMutualAuthentication,
                        ServerCertificate = expectMutualAuthentication ? _serverCertificate : _selfSignedCertificate,
                        RemoteCertificateValidationCallback = delegate { return true; },
                        EnabledSslProtocols = protocol
                    };

                    await TestConfiguration.WhenAllOrAnyFailedWithTimeout(
                        client.AuthenticateAsClientAsync(clientOptions),
                        server.AuthenticateAsServerAsync(serverOptions));

                    // mutual authentication should only be set if server required client cert
                    Assert.Equal(expectMutualAuthentication, server.IsMutuallyAuthenticated);
                    Assert.Equal(expectMutualAuthentication, client.IsMutuallyAuthenticated);
                };
            }
        }

        private static bool AllowAnyCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        private X509Certificate ClientCertSelectionCallback(
            object sender,
            string targetHost,
            X509CertificateCollection localCertificates,
            X509Certificate remoteCertificate,
            string[] acceptableIssuers)
        {
            return _clientCertificate;
        }
    }
}
