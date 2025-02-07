/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Python.Runtime;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Lean.Engine.HistoricalData;
using QuantConnect.Packets;
using QuantConnect.Tests.Common.Data.UniverseSelection;
using QuantConnect.Tests.Engine.DataFeeds;

namespace QuantConnect.Tests.Algorithm.Framework.Portfolio
{
    [TestFixture]
    public class MeanVarianceOptimizationPortfolioConstructionModelTests
    {
        private DateTime _nowUtc;
        private QCAlgorithm _algorithm;

        [SetUp]
        public virtual void SetUp()
        {
            _nowUtc = new DateTime(2013, 10, 8);
            _algorithm = new QCAlgorithm();
            _algorithm.SetFinishedWarmingUp();
            _algorithm.SetPandasConverter();
            _algorithm.SubscriptionManager.SetDataManager(new DataManagerStub(_algorithm));
            _algorithm.SetDateTime(_nowUtc.ConvertToUtc(_algorithm.TimeZone));
            var historyProvider = new SubscriptionDataReaderHistoryProvider();
            _algorithm.SetHistoryProvider(historyProvider);

            historyProvider.Initialize(new HistoryProviderInitializeParameters(
                new BacktestNodePacket(),
                new Api.Api(),
                TestGlobals.DataProvider,
                new SingleEntryDataCacheProvider(new DefaultDataProvider()),
                TestGlobals.MapFileProvider,
                TestGlobals.FactorFileProvider,
                i => { },
                true,
                new DataPermissionManager()));
        }

        [TestCase(Language.CSharp, PortfolioBias.Long, 0.1, -0.1)]
        [TestCase(Language.Python, PortfolioBias.Long, 0.1, -0.1)]
        [TestCase(Language.CSharp, PortfolioBias.Short, -0.1, 0.1)]
        [TestCase(Language.Python, PortfolioBias.Short, -0.1, 0.1)]
        [TestCase(Language.CSharp, PortfolioBias.Long, -0.1, 0.1)]
        [TestCase(Language.Python, PortfolioBias.Long, -0.1, 0.1)]
        [TestCase(Language.CSharp, PortfolioBias.Short, 0.1, -0.1)]
        [TestCase(Language.Python, PortfolioBias.Short, 0.1, -0.1)]
        public void PortfolioBiasIsRespected(Language language, PortfolioBias bias, double magnitude1, double magnitude2)
        {
            var targets = GeneratePortfolioTargets(language, InsightDirection.Up, InsightDirection.Down, magnitude1, magnitude2, bias);
            
            foreach (var target in targets)
            {
                QuantConnect.Logging.Log.Trace($"{target.Symbol}: {target.Quantity}");
                if (target.Quantity == 0)
                {
                    continue;
                }
                Assert.AreEqual(Math.Sign((int)bias), Math.Sign(target.Quantity));
            }
        }

        [TestCase(Language.CSharp, InsightDirection.Up, InsightDirection.Up, 0, 0, 4155, 2493)]
        [TestCase(Language.Python, InsightDirection.Up, InsightDirection.Up, 0, 0, 4155, 2493)]
        [TestCase(Language.CSharp, InsightDirection.Up, InsightDirection.Down, 0.1, 0.05, 4155, 2493)]
        [TestCase(Language.Python, InsightDirection.Up, InsightDirection.Down, 0.1, 0.05, 4155, 2493)]
        [TestCase(Language.CSharp, InsightDirection.Up, InsightDirection.Up, 0.1, 0, 4155, 2493)]
        [TestCase(Language.Python, InsightDirection.Up, InsightDirection.Up, 0.1, 0, 4155, 2493)]
        [TestCase(Language.CSharp, InsightDirection.Up, InsightDirection.Down, 0, 0.1, 4155, 2493)]
        [TestCase(Language.Python, InsightDirection.Up, InsightDirection.Down, 0, 0.1, 4155, 2493)]
        public void CorrectWeightings(Language language,
                                      InsightDirection direction1,
                                      InsightDirection direction2,
                                      double? magnitude1,
                                      double? magnitude2,
                                      decimal expectedQty1,
                                      decimal expectedQty2)
        {
            var targets = GeneratePortfolioTargets(language, direction1, direction2, magnitude1, magnitude2);
            var quantities = targets.ToDictionary(target => {
                QuantConnect.Logging.Log.Trace($"{target.Symbol}: {target.Quantity}");
                return target.Symbol.Value;
            },
            target => target.Quantity);

            Assert.AreEqual(expectedQty1, quantities["AAPL"]);
            Assert.AreEqual(expectedQty2, quantities.ContainsKey("SPY") ? quantities["SPY"] : 0);
        }

        [Test]
        [TestCase(Language.CSharp)]
        [TestCase(Language.Python)]
        public void DoesNotReturnTargetsIfNoInsightMagnitude(Language language)
        {
            SetPortfolioConstruction(language, PortfolioBias.LongShort);

            var appl = _algorithm.AddEquity("AAPL");

            var insights = new[]
            {
                new Insight(_nowUtc, appl.Symbol, TimeSpan.FromDays(1), InsightType.Price, InsightDirection.Up, null, null)
            };

            var actualTargets = _algorithm.PortfolioConstruction.CreateTargets(_algorithm, insights).ToList();
            Assert.AreEqual(0, actualTargets.Count);
        }

        [TestCase(Language.CSharp, InsightDirection.Up, InsightDirection.Up, 0.1, 0.1)]
        [TestCase(Language.Python, InsightDirection.Up, InsightDirection.Up, 0.1, 0.1)]
        [TestCase(Language.CSharp, InsightDirection.Up, InsightDirection.Up, 0.1, -0.1)]
        [TestCase(Language.Python, InsightDirection.Up, InsightDirection.Up, 0.1, -0.1)]
        [TestCase(Language.CSharp, InsightDirection.Up, InsightDirection.Up, 0.1, 0)]
        [TestCase(Language.Python, InsightDirection.Up, InsightDirection.Up, 0.1, 0)]
        [TestCase(Language.CSharp, InsightDirection.Up, InsightDirection.Down, 0.1, 0.1)]
        [TestCase(Language.Python, InsightDirection.Up, InsightDirection.Down, 0.1, 0.1)]
        public void ObeyBudgetConstraint(Language language,
                                         InsightDirection direction1,
                                         InsightDirection direction2,
                                         double? magnitude1,
                                         double? magnitude2)
        {
            var targets = GeneratePortfolioTargets(language, direction1, direction2, magnitude1, magnitude2);
            var totalCost = targets.Sum(x => Math.Abs(x.Quantity) * 10);    // Set market price at $10 in the helper method
            Assert.LessOrEqual(totalCost, _algorithm.Portfolio.TotalPortfolioValue);
        }

        protected void SetPortfolioConstruction(Language language, PortfolioBias bias)
        {
            var model = GetPortfolioConstructionModel(language, Resolution.Daily, bias);
            _algorithm.SetPortfolioConstruction(model);

            foreach (var kvp in _algorithm.Portfolio)
            {
                kvp.Value.SetHoldings(kvp.Value.Price, 0);
            }

            var changes = SecurityChangesTests.AddedNonInternal(_algorithm.Securities.Values.ToArray());
            _algorithm.PortfolioConstruction.OnSecuritiesChanged(_algorithm, changes);
        }

        public IPortfolioConstructionModel GetPortfolioConstructionModel(Language language, Resolution resolution, PortfolioBias bias)
        {
            if (language == Language.CSharp)
            {
                return new MeanVarianceOptimizationPortfolioConstructionModel(resolution, bias, 1, 63, Resolution.Daily, 0.0001);
            }

            using (Py.GIL())
            {
                const string name = nameof(MeanVarianceOptimizationPortfolioConstructionModel);
                var instance = Py.Import(name).GetAttr(name)
                    .Invoke(((int)resolution).ToPython(), ((int)bias).ToPython(), 1.ToPython(), 63.ToPython(), ((int)Resolution.Daily).ToPython(), 0.0001.ToPython());
                return new PortfolioConstructionModelPythonWrapper(instance);
            }
        }

        private IEnumerable<IPortfolioTarget> GeneratePortfolioTargets(Language language, InsightDirection direction1, InsightDirection direction2, 
                                                                       double? magnitude1, double? magnitude2, PortfolioBias bias = PortfolioBias.LongShort)
        {
            SetPortfolioConstruction(language, bias);

            var aapl = _algorithm.AddEquity("AAPL");
            var spy = _algorithm.AddEquity("SPY");

            aapl.SetMarketPrice(new Tick(_nowUtc, aapl.Symbol, 10, 10));
            spy.SetMarketPrice(new Tick(_nowUtc, spy.Symbol, 10, 10));
            aapl.SetMarketPrice(new Tick(_nowUtc.AddDays(1), aapl.Symbol, 8, 8));
            spy.SetMarketPrice(new Tick(_nowUtc.AddDays(1), spy.Symbol, 15, 15));
            aapl.SetMarketPrice(new Tick(_nowUtc.AddDays(2), aapl.Symbol, 12, 12));
            spy.SetMarketPrice(new Tick(_nowUtc.AddDays(2), spy.Symbol, 20, 20));

            var insights = new[]
            {
                new Insight(_nowUtc, aapl.Symbol, TimeSpan.FromDays(1), InsightType.Price, direction1, magnitude1, null),
                new Insight(_nowUtc, spy.Symbol, TimeSpan.FromDays(1), InsightType.Price, direction2, magnitude2, null),
            };
            _algorithm.PortfolioConstruction.OnSecuritiesChanged(_algorithm, SecurityChangesTests.AddedNonInternal(aapl, spy));

            return _algorithm.PortfolioConstruction.CreateTargets(_algorithm, insights);
        }
    }
}
