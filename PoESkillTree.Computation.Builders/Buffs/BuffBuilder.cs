﻿using System;
using System.Collections.Generic;
using System.Linq;
using PoESkillTree.Computation.Builders.Actions;
using PoESkillTree.Computation.Builders.Effects;
using PoESkillTree.Computation.Builders.Entities;
using PoESkillTree.Computation.Builders.Stats;
using PoESkillTree.Computation.Common;
using PoESkillTree.Computation.Common.Builders;
using PoESkillTree.Computation.Common.Builders.Actions;
using PoESkillTree.Computation.Common.Builders.Buffs;
using PoESkillTree.Computation.Common.Builders.Conditions;
using PoESkillTree.Computation.Common.Builders.Effects;
using PoESkillTree.Computation.Common.Builders.Entities;
using PoESkillTree.Computation.Common.Builders.Resolving;
using PoESkillTree.Computation.Common.Builders.Stats;
using PoESkillTree.Utils;

namespace PoESkillTree.Computation.Builders.Buffs
{
    public class BuffBuilder : EffectBuilder, IBuffBuilder
    {
        public BuffBuilder(IStatFactory statFactory, ICoreBuilder<string> identity) : base(statFactory, identity)
        {
        }

        public override IEffectBuilder Resolve(ResolveContext context) =>
            new BuffBuilder(StatFactory, Identity.Resolve(context));

        public IStatBuilder NotAsBuffOn(IEntityBuilder target) =>
            InternalOn(target);

        public IStatBuilder Effect => EffectOn(EntityBuilder.AllEntities);

        public IStatBuilder EffectOn(IEntityBuilder target)
        {
            return new StatBuilder(StatFactory, FromStatFactory((ps, s, i) => BuildStats(ps, s, i)));

            IEnumerable<IStat> BuildStats(BuildParameters parameters, Entity source, string identity)
                => target.Build(parameters.ModifierSourceEntity)
                    .Select(t => BuildEffectStat(source, t, identity));
        }

        public IActionBuilder Action =>
            new ActionBuilder(StatFactory, Identity, new ModifierSourceEntityBuilder());

        public override IStatBuilder On(IEntityBuilder target) =>
            base.On(target)
                .CombineWith(new StatBuilder(StatFactory, FromStatFactory(BuildBuffActiveStat)))
                .CombineWith(new StatBuilder(StatFactory, FromStatFactory((ps, t, i) => BuildBuffSourceStat(ps, t, i))))
                .For(target);

        private IStat BuildBuffSourceStat(BuildParameters parameters, Entity target, string identity) =>
            BuildBuffSourceStat(parameters.ModifierSourceEntity, target, identity);

        public IConditionBuilder IsOn(IEntityBuilder source, IEntityBuilder target) =>
            IsOn(target).And(IsFromSource(source, target));

        private IConditionBuilder IsFromSource(IEntityBuilder source, IEntityBuilder target)
        {
            var core = FromStatFactory((ps, t, i) => BuildStats(ps, t, i));
            return new StatBuilder(StatFactory, core).For(target).IsSet;

            IEnumerable<IStat> BuildStats(BuildParameters parameters, Entity t, string identity)
                => source.Build(parameters.ModifierSourceEntity)
                    .Select(s => BuildBuffSourceStat(s, t, identity));
        }

        public override IStatBuilder AddStat(IStatBuilder stat) => AddStatForSource(stat, EntityBuilder.AllEntities);

        public IStatBuilder AddStatForSource(IStatBuilder stat, IEntityBuilder source)
        {
            var baseCoreBuilder = new StatBuilderAdapter(base.AddStat(stat));
            var coreBuilder = new StatBuilderWithValueConverter(baseCoreBuilder,
                (ps, target) => BuildAddStatMultiplier(source.Build(ps.ModifierSourceEntity), target),
                (l, r) => l.Multiply(r));
            return new StatBuilder(StatFactory, coreBuilder);
        }

        private IValue BuildAddStatMultiplier(IReadOnlyCollection<Entity> possibleSources, Entity target)
        {
            var identity = Build();
            var buffActiveValue = new StatValue(BuildBuffActiveStat(target, identity));
            var buffSourceValues = possibleSources.ToDictionary(Funcs.Identity,
                e => new StatValue(BuildBuffSourceStat(e, target, identity)));
            var buffEffectValues = possibleSources.ToDictionary(Funcs.Identity,
                e => new StatValue(BuildEffectStat(e, target, identity)));

            return new FunctionalValue(Calculate,
                $"AddStatMultiplier(buffActive:{buffActiveValue}, buffSources:{string.Join(",", buffSourceValues)}, " +
                $"buffEffects:{string.Join(",", buffEffectValues)})");

            NodeValue? Calculate(IValueCalculationContext context)
            {
                if (!buffActiveValue.Calculate(context).IsTrue())
                    return new NodeValue(1);

                // If multiple entities apply the same (de-)buff, it depends on the buff which one would actually apply.
                // Because that shouldn't happen in these calculations, simply the first one is taken.
                var sourcEntity = possibleSources.First(e => buffSourceValues[e].Calculate(context).IsTrue());
                return buffEffectValues[sourcEntity].Calculate(context);
            }
        }

        private ICoreStatBuilder FromStatFactory(Func<Entity, string, IStat> statFactory) =>
            new CoreStatBuilderFromCoreBuilder<string>(Identity, statFactory);

        private ICoreStatBuilder FromStatFactory(Func<BuildParameters, Entity, string, IStat> statFactory) =>
            new CoreStatBuilderFromCoreBuilder<string>(Identity, statFactory);

        private ICoreStatBuilder FromStatFactory(CoreStatBuilderFromCoreBuilder<string>.StatFactory statFactory) =>
            new CoreStatBuilderFromCoreBuilder<string>(Identity, statFactory);

        private IStat BuildEffectStat(Entity source, Entity target, string identity) =>
            StatFactory.BuffEffect(source, target, identity);

        private IStat BuildBuffActiveStat(Entity target, string identity) =>
            StatFactory.BuffIsActive(target, identity);

        private IStat BuildBuffSourceStat(Entity source, Entity target, string identity) =>
            StatFactory.BuffSourceIs(source, target, identity);
    }
}