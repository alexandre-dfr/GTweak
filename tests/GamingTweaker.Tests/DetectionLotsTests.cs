using System.Linq;
using GamingTweaker.Core;
using Xunit;

namespace GamingTweaker.Tests
{
    public class DetectionLotsTests
    {
        private static CatalogItem[] Items(int n) => Enumerable.Range(0, n).Select(i => new CatalogItem { Id = "x." + i, Detect = "'NotApplied'" }).ToArray();

        [Fact]
        public void Lots_en_tour_de_role_sans_perte_ni_doublon()
        {
            var items = Items(83);
            var lots = Detection.Lots(items, 4);
            Assert.Equal(4, lots.Count);
            Assert.Equal(items.Select(i => i.Id).OrderBy(x => x), lots.SelectMany(l => l).Select(i => i.Id).OrderBy(x => x));
            Assert.True(lots.Max(l => l.Count) - lots.Min(l => l.Count) <= 1);
        }

        [Fact]
        public void Peu_d_options_un_seul_processus()
        {
            Assert.Single(Detection.Lots(Items(1), 4));
            Assert.Single(Detection.Lots(Items(8), 4));
            Assert.Equal(2, Detection.Lots(Items(9), 4).Count);
        }

        [Fact]
        public async System.Threading.Tasks.Task Detection_progressive_signale_chaque_option()
        {
            var items = Items(12).Select((it, i) => { it.Detect = i % 3 == 0 ? "'Applied'" : "'NotApplied'"; return it; }).ToArray();
            var vus = new System.Collections.Concurrent.ConcurrentDictionary<string, DetectState>();
            var r = await Detection.RunAsync(items, System.TimeSpan.FromSeconds(120), (id, st) => vus[id] = st);
            Assert.Equal(12, vus.Count);
            foreach (var kv in r) Assert.Equal(kv.Value, vus[kv.Key]);
        }

        [Fact]
        public async System.Threading.Tasks.Task Detection_parallele_rend_un_etat_par_option()
        {
            var items = Items(20).Select((it, i) => { it.Detect = i % 2 == 0 ? "'Applied'" : "'NotApplied'"; return it; }).ToArray();
            var r = await Detection.RunAsync(items);
            Assert.Equal(20, r.Count);
            for (int i = 0; i < 20; i++) Assert.Equal(i % 2 == 0 ? DetectState.Applied : DetectState.NotApplied, r["x." + i]);
        }
    }
}
