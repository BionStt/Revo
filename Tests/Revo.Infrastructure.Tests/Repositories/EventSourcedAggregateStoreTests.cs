﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Revo.Infrastructure.EventSourcing;
using Revo.Infrastructure.Repositories;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Revo.Core.Events;
using Revo.DataAccess.Entities;
using Revo.Domain.Entities;
using Revo.Domain.Entities.Basic;
using Revo.Domain.Entities.EventSourcing;
using Revo.Domain.Events;
using Revo.Infrastructure.Events;
using Revo.Infrastructure.EventStores;
using Revo.Testing.Core;
using Revo.Testing.Infrastructure;
using Xunit;

namespace Revo.Infrastructure.Tests.Repositories
{
    public class EventSourcedAggregateStoreTests
    {
        private EventSourcedAggregateStore sut;

        private readonly IPublishEventBuffer publishEventBuffer;
        private readonly IEventStore eventStore;
        private readonly IEntityTypeManager entityTypeManager;
        private readonly IRepositoryFilter repositoryFilter1;
        private readonly IRepositoryFilter repositoryFilter2;
        private readonly IEventMessageFactory eventMessageFactory;

        private Guid entityId = Guid.NewGuid();
        private Guid entity2Id = Guid.NewGuid();
        private Guid entity3Id = Guid.NewGuid();
        private Guid entity4Id = Guid.NewGuid();
        private Guid entityClassId = Guid.NewGuid();
        private Guid entity2ClassId = Guid.NewGuid();
        private Guid entity3ClassId = Guid.NewGuid();

        private Dictionary<Guid, IReadOnlyDictionary<string, string>> entityMetadata;
        private Dictionary<Guid, IReadOnlyCollection<IEventStoreRecord>> entityEvents;

        public EventSourcedAggregateStoreTests()
        {
            publishEventBuffer = Substitute.For<IPublishEventBuffer>();
            eventStore = Substitute.For<IEventStore>();
            entityTypeManager = Substitute.For<IEntityTypeManager>();
            repositoryFilter1 = Substitute.For<IRepositoryFilter>();
            repositoryFilter2 = Substitute.For<IRepositoryFilter>();

            FakeClock.Setup();

            entityEvents = new Dictionary<Guid, IReadOnlyCollection<IEventStoreRecord>>()
            {
                {
                    entity2Id,
                    new List<IEventStoreRecord>()
                    {
                        new FakeEventStoreRecord()
                        {
                            Event = new SetFooEvent()
                            {
                                AggregateId = entity2Id
                            },
                            StreamSequenceNumber = 1
                        }
                    }
                },
                {
                    entity4Id,
                    new List<IEventStoreRecord>()
                    {
                        new FakeEventStoreRecord()
                        {
                            Event = new SetFooEvent()
                            {
                                AggregateId = entity4Id
                            },
                            StreamSequenceNumber = 1
                        }
                    }
                }
            };

            eventStore.FindEventsAsync(Arg.Any<Guid>())
                .Returns(ci =>
                {
                    var id = ci.ArgAt<Guid>(0);
                    if (entityEvents.TryGetValue(id, out var events))
                    {
                        return events;
                    }

                    return new List<IEventStoreRecord>();
                });


            eventStore.BatchFindEventsAsync(Arg.Any<Guid[]>())
                .Returns(ci =>
                {
                    var ids = ci.ArgAt<Guid[]>(0);
                    var result = new Dictionary<Guid, IReadOnlyCollection<IEventStoreRecord>>();
                    foreach (Guid id in ids)
                    {
                        if (entityEvents.TryGetValue(id, out var events))
                        {
                            result.Add(id, events);
                        }
                    }

                    return result;
                });

            entityMetadata = new Dictionary<Guid, IReadOnlyDictionary<string, string>>()
            {
                {
                    entity2Id,
                    new Dictionary<string, string>()
                    {
                        {"TestKey", "TestValue"},
                        {AggregateEventStreamMetadataNames.ClassId, entity2ClassId.ToString()}
                    }
                },
                {
                    entity3Id,
                    new Dictionary<string, string>()
                    {
                        {"TestKey", "TestValue"},
                        {AggregateEventStreamMetadataNames.ClassId, entity3ClassId.ToString()}
                    }
                },
                {
                    entity4Id,
                    new Dictionary<string, string>()
                    {
                        {"TestKey", "TestValue"},
                        {AggregateEventStreamMetadataNames.ClassId, entity2ClassId.ToString()}
                    }
                }
            };

            eventStore.FindStreamMetadataAsync(Arg.Any<Guid>())
                .Returns(ci =>
                {
                    var id = ci.ArgAt<Guid>(0);
                    if (entityMetadata.TryGetValue(id, out var metadata))
                    {
                        return metadata;
                    }

                    return new Dictionary<string, string>();
                });

            eventStore.BatchFindStreamMetadataAsync(Arg.Any<Guid[]>())
                .Returns(ci =>
                {
                    var ids = ci.ArgAt<Guid[]>(0);
                    var result = new Dictionary<Guid, IReadOnlyDictionary<string, string>>();
                    foreach (Guid id in ids)
                    {
                        if (entityMetadata.TryGetValue(id, out var metadata))
                        {
                            result.Add(id, metadata);
                        }
                    }

                    return result;
                });

            eventStore.PushEventsAsync(Guid.Empty, null, null).ReturnsForAnyArgs(ci =>
            {
                var events = ci.ArgAt<IEnumerable<IUncommittedEventStoreRecord>>(1);
                return events.Select(x => new FakeEventStoreRecord()
                {
                    AdditionalMetadata = x.Metadata,
                    Event = x.Event,
                    EventId = Guid.NewGuid(),
                    StoreDate = DateTimeOffset.Now,
                    StreamSequenceNumber = 0
                }).ToList();
            });

            DomainClassInfo[] domainClasses = new[]
            {
                new DomainClassInfo(entityClassId, null, typeof(MyEntity)),
                new DomainClassInfo(entity2ClassId, null, typeof(MyEntity2)),
                new DomainClassInfo(entity3ClassId, null, typeof(MyEntity3LoadsAsDeleted))
            };

            entityTypeManager.GetClassInfoByClassId(Guid.Empty)
                .ReturnsForAnyArgs(ci => domainClasses.Single(x => x.Id == ci.Arg<Guid>()));
            entityTypeManager.TryGetClassInfoByClrType(null)
                .ReturnsForAnyArgs(ci => domainClasses.SingleOrDefault(x => x.ClrType == ci.Arg<Type>()));
            entityTypeManager.GetClassInfoByClrType(null)
                .ReturnsForAnyArgs(ci => domainClasses.Single(x => x.ClrType == ci.Arg<Type>()));

            eventMessageFactory = Substitute.For<IEventMessageFactory>();
            eventMessageFactory.CreateMessageAsync(null).ReturnsForAnyArgs(ci =>
            {
                var @event = ci.ArgAt<IEvent>(0);
                Type messageType = typeof(EventMessageDraft<>).MakeGenericType(@event.GetType());
                IEventMessageDraft messageDraft = (IEventMessageDraft)messageType.GetConstructor(new[] { @event.GetType() }).Invoke(new[] { @event });
                messageDraft.SetMetadata("TestKey", "TestValue");
                return messageDraft;
            }); // TODO something more lightweight?

            sut = new EventSourcedAggregateStore(eventStore, entityTypeManager, publishEventBuffer,
                new IRepositoryFilter[] { }, eventMessageFactory, new EntityFactory());
        }

        [Fact]
        public void Add_CreatesStreamAndMetadata()
        {
            var entity = new MyEntity(entityId, 5);
            sut.Add(entity);
            eventStore.Received(1).AddStream(entity.Id);
            eventStore.Received(1).SetStreamMetadata(entity.Id,
                Arg.Is<IReadOnlyDictionary<string, string>>(x => x[AggregateEventStreamMetadataNames.ClassId] == entityClassId.ToString()));
        }

        [Fact]
        public async Task AddThenGet_ReturnsTheSame()
        {
            var entity = new MyEntity(entityId, 5);
            sut.Add(entity);
            var entity2 = await sut.GetAsync<MyEntity>(entity.Id);

            Assert.Equal(entity, entity2);
        }

        [Fact]
        public async Task FindAsync_ReturnsNullIfNotFound()
        {
            Guid nonexistentId = Guid.Parse("E9C6FCB7-A832-4534-921D-843B6E910CBD");
            eventStore.FindEventsAsync(nonexistentId).Returns(new List<IEventStoreRecord>());
            eventStore.FindStreamMetadataAsync(nonexistentId).Returns((IReadOnlyDictionary<string, string>)null);

            var result = await sut.FindAsync<MyEntity>(nonexistentId);
            result.Should().BeNull();
        }

        [Fact]
        public async Task FindAsync_ReturnsNullIfDifferentType()
        {
            var result = await sut.FindAsync<MyEntity3LoadsAsDeleted>(entity2Id);
            result.Should().BeNull();
        }

        [Fact]
        public async Task FindManyAsync_ReturnsOnlyThoseFound()
        {
            Guid nonexistentId = Guid.Parse("E9C6FCB7-A832-4534-921D-843B6E910CBD");
            eventStore.FindEventsAsync(nonexistentId).Returns(new List<IEventStoreRecord>());
            eventStore.FindStreamMetadataAsync(nonexistentId).Returns((IReadOnlyDictionary<string, string>)null);

            var result = await sut.FindManyAsync<MyEntity>(nonexistentId, entity2Id);
            result.Should().HaveCount(1);
            result.Should().Contain(x => x.Id == entity2Id);
        }

        [Fact]
        public async Task FindManyAsync_ReturnsNullIfDifferentType()
        {
            var result = await sut.FindManyAsync<MyEntity2>(entity2Id, entity3Id);
            result.Should().HaveCount(1);
            result.Should().Contain(x => x.Id == entity2Id);
        }

        [Fact]
        public async Task GetAsync_ReturnsCorrectAggregate()
        {
            var entity = await sut.GetAsync<MyEntity2>(entity2Id);

            Assert.Equal(entity2Id, entity.Id);

            Assert.Equal(1, entity.LoadedEvents.Count);
            Assert.IsType<SetFooEvent>(entity.LoadedEvents[0]);
            Assert.Equal(1, entity.Version);
        }

        [Fact]
        public async Task GetAsync_CachesReturnedEntities()
        {
            var entity1 = await sut.GetAsync<MyEntity2>(entity2Id);
            var entity2 = await sut.GetAsync<MyEntity2>(entity2Id);

            Assert.NotNull(entity1);
            Assert.Equal(entity1, entity2);
        }

        [Fact]
        public async Task GetAsync_ReturnsCorrectDescendant()
        {
            var entity = await sut.GetAsync<MyEntity>(entity2Id);
            Assert.IsType<MyEntity2>(entity);
        }

        [Fact]
        public async Task GetAsync_ThrowsIfDeleted()
        {
            eventStore.FindEventsAsync(entity3Id)
                .Returns(new List<IEventStoreRecord>()
                {
                    new FakeEventStoreRecord()
                    {
                        Event = new SetFooEvent()
                        {
                            AggregateId = entityId
                        },
                        StreamSequenceNumber = 1
                    }
                });

            await Assert.ThrowsAsync<EntityDeletedException>(async () =>
            {
                await sut.GetAsync<MyEntity3LoadsAsDeleted>(entity3Id);
            });
        }

        [Fact]
        public async Task GetAsync_ThrowsIfDeletedAfterLoading()
        {
            var entity = await sut.GetAsync<MyEntity2>(entity2Id);
            entity.IsDeleted = true;

            await Assert.ThrowsAsync<EntityDeletedException>(async () =>
            {
                await sut.GetAsync<MyEntity2>(entity2Id);
            });
        }

        [Fact]
        public async Task GetAsync_ThrowsIfNotFound()
        {
            Guid nonexistentId = Guid.Parse("E9C6FCB7-A832-4534-921D-843B6E910CBD");
            eventStore.FindEventsAsync(nonexistentId).Throws(new EntityNotFoundException());
            eventStore.FindStreamMetadataAsync(nonexistentId).Throws(new EntityNotFoundException());

            await Assert.ThrowsAsync<EntityNotFoundException>(async () =>
            {
                await sut.GetAsync<MyEntity>(nonexistentId);
            });
        }

        [Fact]
        public async Task GetAsync_ThrowsIfDifferentType()
        {
            await Assert.ThrowsAsync<EntityNotFoundException>(async () =>
            {
                await sut.GetAsync<MyEntity3LoadsAsDeleted>(entity2Id);
            });
        }

        [Fact]
        public async Task GetManyAsync_ReturnsCorrectAggregates()
        {
            var entities = await sut.GetManyAsync<MyEntity2>(entity2Id, entity4Id);

            entities.Should().HaveCount(2);
            entities.Should().NotContainNulls();
            entities.Should().Contain(x => x.Id == entity2Id);
            entities.Should().Contain(x => x.Id == entity4Id);
        }

        [Fact]
        public async Task GetManyAsync_CachesReturnedEntities()
        {
            var entities = await sut.GetManyAsync<MyEntity2>(entity2Id, entity4Id);
            var entities2 = await sut.GetManyAsync<MyEntity2>(entity2Id, entity4Id);
            entities2.Should().Equal(entities);
        }

        [Fact]
        public async Task GetManyAsync_ReturnsCorrectDescendant()
        {
            var entities = await sut.GetManyAsync<MyEntity>(entity2Id, entity4Id);
            entities.Should().AllBeOfType<MyEntity2>();
        }

        [Fact]
        public void SaveChangesAsync_AddTwiceWithSameIdsThrows()
        {
            var entity = new MyEntity(entityId, 5);
            sut.Add(entity);

            var entity2 = new MyEntity(entityId, 5);

            Assert.Throws<ArgumentException>(() => sut.Add(entity2));
        }

        [Fact]
        public async Task SaveChangesAsync_SavesNewAggregate()
        {
            var entity = new MyEntity(entityId, 5);
            sut.Add(entity);

            entity.UncommittedEvents = new List<DomainAggregateEvent>()
            {
                new SetFooEvent()
                {
                    AggregateId = entityId
                }
            };

            List<IUncommittedEventStoreRecord> eventsRecords = new List<IUncommittedEventStoreRecord>()
            {
                new UncommitedEventStoreRecord(entity.UncommittedEvents.ElementAt(0),
                    new Dictionary<string, string>()
                    {
                        { "TestKey", "TestValue" },
                        { BasicEventMetadataNames.AggregateClassId, entityClassId.ToString() }
                    })
            };

            IEnumerable<IUncommittedEventStoreRecord> pushedEvents = null;
            eventStore.WhenForAnyArgs(x => x.PushEventsAsync(Guid.Empty, null, null))
                .Do(ci => pushedEvents = ci.ArgAt<IEnumerable<IUncommittedEventStoreRecord>>(1));

            await sut.SaveChangesAsync();

            eventStore.Received(1).AddStream(entityId);
            eventStore.Received(1).PushEventsAsync(entityId, Arg.Any<IEnumerable<IUncommittedEventStoreRecord>>(), 0);
            eventStore.Received(1).CommitChangesAsync();

            pushedEvents.Should().BeEquivalentTo(eventsRecords);
        }

        [Fact]
        public async Task SaveChangesAsync_PushesEventsForPublishing()
        {
            var entity = new MyEntity(entityId, 5);
            sut.Add(entity);

            var event1 = new SetFooEvent()
            {
                AggregateId = entityId
            };

            entity.UncommittedEvents = new List<DomainAggregateEvent>()
            {
                event1
            };

            await sut.SaveChangesAsync();

            publishEventBuffer.Received(1).PushEvent(Arg.Is<IEventMessage>(x => x.Event == event1));
        }

        [Fact]
        public async Task SaveChangesAsync_PushedEventsHaveMetadata()
        {
            var entity = new MyEntity(entityId, 5);
            sut.Add(entity);

            var event1 = new SetFooEvent()
            {
                AggregateId = entityId
            };

            entity.UncommittedEvents = new List<DomainAggregateEvent>()
            {
                event1
            };

            List<IEventMessage> eventMessages = new List<IEventMessage>();

            publishEventBuffer.WhenForAnyArgs(x => x.PushEvent(null)).Do(ci =>
            {
                eventMessages.Add(ci.ArgAt<IEventMessage>(0));
            });

            await sut.SaveChangesAsync();

            eventMessages.Should().HaveCount(1);
            eventMessages[0].Metadata.Should().Contain(x => x.Key == "TestKey" && x.Value == "TestValue");
            eventMessages[0].Metadata.Should().Contain(x => x.Key == BasicEventMetadataNames.AggregateClassId && x.Value == entityClassId.ToString());
            // TODO test also other metadata values?
        }

        [Fact]
        public async Task SaveChangesAsync_CommitsAggregate()
        {
            var entity = new MyEntity(entityId, 5);
            sut.Add(entity);

            entity.UncommittedEvents = new List<DomainAggregateEvent>()
            {
                new SetFooEvent()
                {
                    AggregateId = entityId
                }
            };

            await sut.SaveChangesAsync();
            Assert.Equal(1, entity.Version);
        }

        [Fact]
        public async Task SaveChangesAsync_CommitsOnlyChangedAggregates()
        {
            var entity = new MyEntity(entityId, 5);
            sut.Add(entity);

            await sut.SaveChangesAsync();
            Assert.Equal(0, entity.Version);
        }

        [Fact]
        public async Task DefaultFilters_GetsInitialFilters()
        {
            sut = new EventSourcedAggregateStore(eventStore, entityTypeManager, publishEventBuffer,
                new[] { repositoryFilter1 }, eventMessageFactory, new EntityFactory());
            Assert.True(sut.DefaultFilters.SequenceEqual(new[] { repositoryFilter1 }));
        }

        [Fact]
        public async Task GetAsync_FilterGetsCalled()
        {
            repositoryFilter1.FilterResult<IAggregateRoot>(null)
                .ReturnsForAnyArgs(ci =>
                    {
                        return ci.ArgAt<IAggregateRoot>(0);
                    }
                    
                    );

            sut = new EventSourcedAggregateStore(eventStore, entityTypeManager, publishEventBuffer,
                new[] { repositoryFilter1 }, eventMessageFactory, new EntityFactory());

            await sut.GetAsync<MyEntity2>(entity2Id);

            repositoryFilter1.Received(1).FilterResult<IAggregateRoot>(Arg.Is<IAggregateRoot>(x => x.Id == entity2Id));
        }

        [Fact]
        public async Task GetAsync_FilterReplacesReturnValue()
        {
            var replacementEntity = new MyEntity2(Guid.NewGuid(), 5);
            repositoryFilter1.FilterResult<IAggregateRoot>(null)
                .ReturnsForAnyArgs(replacementEntity);

            sut = new EventSourcedAggregateStore(eventStore, entityTypeManager, publishEventBuffer,
                new[] { repositoryFilter1 }, eventMessageFactory, new EntityFactory());

            Assert.Equal(replacementEntity, await sut.GetAsync<MyEntity2>(entity2Id));
        }

        [Fact]
        public async Task SaveChangesAsync_FiltersAdded()
        {
            sut = new EventSourcedAggregateStore(eventStore, entityTypeManager, publishEventBuffer,
                new[] { repositoryFilter1 }, eventMessageFactory, new EntityFactory());

            var entity = new MyEntity(entityId, 5);
            sut.Add(entity);

            entity.UncommittedEvents = new List<DomainAggregateEvent>()
            {
                new SetFooEvent()
                {
                    AggregateId = entityId
                }
            };

            await sut.SaveChangesAsync();

            repositoryFilter1.Received(1).FilterAdded(entity);
        }

        [Fact]
        public async Task SaveChangesAsync_FiltersModified()
        {
            sut = new EventSourcedAggregateStore(eventStore, entityTypeManager, publishEventBuffer,
                new[] { repositoryFilter1 }, eventMessageFactory, new EntityFactory());

            var entity = new MyEntity(entityId, 5);
            sut.Add(entity);

            entity.UncommittedEvents = new List<DomainAggregateEvent>()
            {
                new SetFooEvent()
                {
                    AggregateId = entityId
                }
            };

            entity.Version++;

            await sut.SaveChangesAsync();

            repositoryFilter1.Received(1).FilterModified(entity);
        }

        [Fact]
        public async Task CanHandleAggregateType_TrueForEventSourced()
        {
            Assert.True(sut.CanHandleAggregateType(typeof(TestAggregate)));
        }

        [Fact]
        public async Task CanHandleAggregateType_FalseForOther()
        {
            Assert.False(sut.CanHandleAggregateType(typeof(NonEventAggregate)));
        }

        public class TestAggregate : EventSourcedAggregateRoot
        {
            public TestAggregate(Guid id) : base(id)
            {
            }
        }

        public class NonEventAggregate : BasicAggregateRoot
        {
            public NonEventAggregate()
            { 
            }
        }

        public class SetFooEvent : DomainAggregateEvent
        {
        }

        public class MyEntity : IEventSourcedAggregateRoot
        {
            public MyEntity(Guid id, int foo) : this(id)
            {
                if (foo != 5)
                {
                    throw new InvalidOperationException();
                }
            }

            protected MyEntity(Guid id)
            {
                Id = id;
            }

            public Guid Id { get; private set; }
            public bool IsDeleted { get; set; }

            public IReadOnlyCollection<DomainAggregateEvent> UncommittedEvents { get; set; } =
                new List<DomainAggregateEvent>();

            public bool IsChanged => UncommittedEvents.Any();
            public int Version { get; set; }

            internal List<DomainAggregateEvent> LoadedEvents;

            public void Commit()
            {
                UncommittedEvents = new List<DomainAggregateEvent>();
                Version++;
            }

            public virtual void LoadState(AggregateState state)
            {
                if (LoadedEvents != null)
                {
                    throw new InvalidOperationException();
                }

                Version = state.Version;
                LoadedEvents = state.Events.ToList();
            }
        }

        public class MyEntity2 : MyEntity
        {
            public MyEntity2(Guid id, int foo) : base(id, foo)
            {
            }

            protected MyEntity2(Guid id) : base(id)
            {
            }
        }

        public class MyEntity3LoadsAsDeleted : MyEntity
        {
            public MyEntity3LoadsAsDeleted(Guid id) : base(id)
            {
            }

            public override void LoadState(AggregateState state)
            {
                base.LoadState(state);
                IsDeleted = true;
            }
        }
    }
}
