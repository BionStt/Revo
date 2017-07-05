﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GTRevo.Infrastructure.Core.Domain.Basic;
using GTRevo.Infrastructure.DataAccess;
using GTRevo.Infrastructure.Repositories;
using GTRevo.Testing.Infrastructure.Repositories;
using NSubstitute;
using Xunit;

namespace GTRevo.Infrastructure.Tests.DataAccess
{
    public class StaticClassifierDatabaseInitializerTests
    {
        private readonly FakeRepository repository;
        private readonly TestClassifierDatabaseInitializer sut;

        public StaticClassifierDatabaseInitializerTests()
        {
            repository = new FakeRepository();
            sut = new TestClassifierDatabaseInitializer();
            sut.Repository = repository;
        }

        [Fact]
        public void Initialize_AddsMissingEntities()
        {
            repository.Add(TestClassifierDatabaseInitializer.First);
            repository.SaveChanges();

            sut.Initialize();

            Assert.Equal(2, repository.FindAll<TestClassifier>().Count());
            Assert.Contains(repository.FindAll<TestClassifier>(), x => x.Id == TestClassifierDatabaseInitializer.Second.Id);
            Assert.False(repository.HasUnsavedChanges());
            Assert.Equal(0, repository.GetRemovedAggregates<TestClassifier>().Count());
        }

        public class TestClassifier : BasicAggregateRoot
        {
            public TestClassifier(Guid id, string name) : base(id)
            {
                Name = name;
            }

            public TestClassifier()
            {
            }

            public string Name { get; private set; }
        }

        public class TestClassifierDatabaseInitializer : StaticClassifierDatabaseInitializer<TestClassifier>
        {
            public static readonly TestClassifier First =
                new TestClassifier(Guid.Parse("{B40A88A0-1D8B-4E72-8420-AA69B1149BB5}"), "First");

            public static readonly TestClassifier Second =
                new TestClassifier(Guid.Parse("{E98D82FB-3E97-46D0-B0EC-F89850279F02}"), "Second");

            public override IEnumerable<TestClassifier> All => new[] { First, Second };
        }

    }
}