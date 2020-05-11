using Centaurus.DAL.Mongo;
using Centaurus.Domain;
using Centaurus.Models;
using Centaurus.Test;
using dotnetstandard_bip32;
using MongoDB.Bson.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using stellar_dotnet_sdk;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;

namespace Centaurus.BanExtension.Test
{
    public class BanExtensionTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            var dbName = "testDB";
            var replicaSet = "centaurusTest";
            var dbPort = 27001;

            MongoDBServerHelper.RunMongoDBServers(new int[] { dbPort }, replicaSet);

            var extensionsPath = ExtensionConfigGenerator.Generate(dbPort, dbName, replicaSet);

            var settings = new AlphaSettings();
            settings.ExtensionsConfigFilePath = Path.GetFullPath(extensionsPath);
            settings.ConnectionString = $"mongodb://localhost:{dbPort}/{dbName}?replicaSet={replicaSet}";
            GlobalInitHelper.SetCommonSettings(settings, TestEnvironment.AlphaKeyPair.SecretSeed);
            settings.Build();

            GlobalInitHelper.Setup(GlobalInitHelper.GetPredefinedClients(), GlobalInitHelper.GetPredefinedAuditors(), settings, new MongoStorage());
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            MongoDBServerHelper.Stop();
            Thread.Sleep(1000);
        }

        [Test]
        public void BannedClientsSaveAndLoadTest()
        {
            var extensionSettings = Global.ExtensionsManager.Extensions.First().Config;
            var banExtension = new BanExtension();
            banExtension.Init(extensionSettings);

            var banTime = DateTime.UtcNow;
            banTime = banTime.AddTicks(-banTime.Ticks % TimeSpan.TicksPerSecond); //ignore milliseconds because it's rounded on saving
            var source = "testClient";
            banExtension.BannedClientsManager.RegisterBan(source, banTime);
            banExtension.BannedClientsManager.UpdateClients();

            var clients = banExtension.BannedClientsManager.Storage.GetBannedClients();
            Assert.AreEqual(clients.Count, 1);

            var firstClient = clients.Values.First();
            Assert.AreEqual(firstClient.Source, source);
            Assert.AreEqual(firstClient.BanCounts, 1);
            Assert.AreEqual(firstClient.Till, BannedClientRecord.CalcTillDate(banTime, banExtension.SingleBanPeriod, banExtension.BanPeriodMultiplier, firstClient.BanCounts));

            banExtension.Dispose();

            Console.WriteLine();
        }

        [Test]
        public void TooManyConnectionsTest()
        {
            try
            {
                for (var i = 0; i < 1000; i++)
                    Global.ExtensionsManager.BeforeNewConnection(new ClientWebSocket(), "test");

                Assert.Fail("Client wasn't block after 1000 connections.");
            }
            catch (ConnectionCloseException exc)
            {
                Assert.AreEqual(WebSocketCloseStatus.PolicyViolation, exc.Status);
            }
        }

        [Test]
        public void TooManyConnectionsFromDifferentIpTest()
        {
            try
            {
                var pubKey = (RawPubKey)KeyPair.Random();
                for (var i = 0; i < 1000; i++)
                {
                    var ip = "127.0.0." + i;
                    var webSocket = new FakeWebSocket();
                    Global.ExtensionsManager.BeforeNewConnection(webSocket, ip);
                    var clientConnection = new AlphaWebSocketConnection(webSocket, ip) { ClientPubKey = pubKey };
                    Global.ExtensionsManager.ConnectionValidated(clientConnection);
                }

                Assert.Fail("Client wasn't block after 1000 connections.");
            }
            catch (ConnectionCloseException exc)
            {
                Assert.AreEqual(WebSocketCloseStatus.PolicyViolation, exc.Status);
            }
        }

        [Test]
        public void BlockOnClientExceptionTest()
        {
            try
            {
                var pubKey = (RawPubKey)KeyPair.Random();
                var webSocket = new FakeWebSocket();
                var clientConnection = new AlphaWebSocketConnection(webSocket, "127.0.0.1") { ClientPubKey = pubKey };
                for (var i = 0; i < 1000; i++)
                {
                    Global.ExtensionsManager.HandleMessageFailed(clientConnection, null, new BaseClientException());
                }

                Assert.Fail("Client wasn't block after 1000 connections.");
            }
            catch (ConnectionCloseException exc)
            {
                Assert.AreEqual(WebSocketCloseStatus.PolicyViolation, exc.Status);
            }
        }
    }
}