﻿#pragma warning disable 169 // ReSharper disable InconsistentNaming
#pragma warning disable IDE1006 // Naming Styles
#pragma warning disable S101 // Types should be named in PascalCase

namespace NEventStore.Persistence.AcceptanceTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NEventStore.Persistence.AcceptanceTests.BDD;
    using FluentAssertions;
#if MSTEST
    using Microsoft.VisualStudio.TestTools.UnitTesting;
#endif
#if NUNIT
    using NUnit.Framework;
    using System.Threading.Tasks;
    using System.Transactions;
    using System.Threading;
    using System.Globalization;
    using System.Diagnostics;
#endif
#if XUNIT
    using Xunit;
    using Xunit.Should;
#endif

    public enum TransactionScopeConcern
    {
        NoTransaction = 0,
        SuppressAmbientTransaction = 1,
        EnlistInAmbientTransaction = 2
    }

    /// <summary>
    /// This testing concern simulated the TrsanctionSuppression and/or re-enlisting in Ambient transaction behavior
    /// </summary>
    public abstract class TransactionConcern : PersistenceEngineConcern
    {
        protected void Reinitialize(TransactionScopeConcern enlistInAmbientTransation)
        {
            switch (enlistInAmbientTransation)
            {
                case TransactionScopeConcern.NoTransaction:
                    Fixture.ScopeOption = null;
                    break;
                case TransactionScopeConcern.SuppressAmbientTransaction:
                    Fixture.ScopeOption = TransactionScopeOption.Suppress;
                    break;
                case TransactionScopeConcern.EnlistInAmbientTransaction:
                    Fixture.ScopeOption = TransactionScopeOption.Required;
                    break;
            }
            Fixture.Initialize(ConfiguredPageSizeForTesting);
        }
    }

    public abstract class MultipleConnectionsWithMultipleTransactionScopes : TransactionConcern
    {
        protected ICommit[] _commits;
        protected const int Loop = 2;
        protected const int StreamsPerTransaction = 20;
        protected readonly IsolationLevel _transationIsolationLevel;
        protected readonly TransactionScopeConcern _enlistInAmbientTransaction;
        protected Exception _thrown;
        protected readonly bool _completeTransaction;

        protected MultipleConnectionsWithMultipleTransactionScopes(
            TransactionScopeConcern enlistInAmbientTransaction,
            IsolationLevel transationIsolationLevel,
            bool completeTransaction
            )
        {
            _transationIsolationLevel = transationIsolationLevel;
            _enlistInAmbientTransaction = enlistInAmbientTransaction;
            _completeTransaction = completeTransaction;
            Reinitialize(enlistInAmbientTransaction);
        }

        protected override void Because()
        {
            _thrown = Catch.Exception(() =>
            Parallel.For(0, Loop, i =>
            {
                var eventStore = new OptimisticEventStore(Persistence, null);

                using (var scope = new TransactionScope(TransactionScopeOption.Required,
                    new TransactionOptions { IsolationLevel = _transationIsolationLevel }
#if NET451 || NETSTANDARD2_0
                , TransactionScopeAsyncFlowOption.Enabled
#endif
                    ))
                {
                    int j;
                    for (j = 0; j < StreamsPerTransaction; j++)
                    {
                        using (var stream = eventStore.OpenStream(i.ToString() + "-" + j.ToString()))
                        {
                            for (int k = 0; k < 10; k++)
                            {
                                stream.Add(new EventMessage { Body = "body" + k });
                            }
                            stream.CommitChanges(Guid.NewGuid());
                        }
                    }
                    if (_completeTransaction)
                    {
                        scope.Complete();
                    }
                }
            })
            );
        }
    }

#if MSTEST
    [TestClass]
#endif
#if NUNIT
    [TestFixture(TransactionScopeConcern.NoTransaction, IsolationLevel.Serializable)] // this will always fail! Serializable prevents multiple transation to perform insert queries simultaneously
    [TestFixture(TransactionScopeConcern.NoTransaction, IsolationLevel.ReadCommitted)]
    [TestFixture(TransactionScopeConcern.SuppressAmbientTransaction, IsolationLevel.Serializable)]
    [TestFixture(TransactionScopeConcern.SuppressAmbientTransaction, IsolationLevel.ReadCommitted)]
    [TestFixture(TransactionScopeConcern.EnlistInAmbientTransaction, IsolationLevel.Serializable)] // this will always fail! Serializable prevents multiple transation to perform insert queries simultaneously
    [TestFixture(TransactionScopeConcern.EnlistInAmbientTransaction, IsolationLevel.ReadCommitted)]
#endif
    public class Multiple_Completing_TransactionScopes_When_EnlistInAmbientTransaction_is_and_IsolationLevel_is :
        MultipleConnectionsWithMultipleTransactionScopes
    {
        public Multiple_Completing_TransactionScopes_When_EnlistInAmbientTransaction_is_and_IsolationLevel_is(
            TransactionScopeConcern enlistInAmbientTransaction,
            IsolationLevel transationIsolationLevel
            ) : base(enlistInAmbientTransaction, transationIsolationLevel, true)
        { }

        [Fact]
        public void should_throw_an_Exception_only_if_no_transaction_or_enlist_in_ambient_transaction_and_IsolationLevel_is_Serializable()
        {
            if (_enlistInAmbientTransaction != TransactionScopeConcern.SuppressAmbientTransaction
                && _transationIsolationLevel == IsolationLevel.Serializable)
            {
                _thrown.Should().BeOfType<AggregateException>();
                _thrown.InnerException.Should().BeOfType<StorageException>();
                // two serializable transactions on the same connection can result in deadlocks.
                _thrown.InnerException.Message.Should().Contain("deadlock");
            }
            else
            {
                _thrown.Should().BeNull();
            }
        }

        [Fact]
        public void Should_have_expected_number_of_commits()
        {
            if (_enlistInAmbientTransaction != TransactionScopeConcern.SuppressAmbientTransaction
                && _transationIsolationLevel == IsolationLevel.Serializable)
            {
                // unpredictable results: some transactions might succeed, other will deadlock
                _commits = Persistence.GetFrom().ToArray();
                _commits.Length.Should().BeGreaterThan(0);
            }
            else
            {
                _commits = Persistence.GetFrom().ToArray();
                _commits.Length.Should().Be(Loop * StreamsPerTransaction);
            }
        }
    }

#if MSTEST
[TestClass]
#endif
#if NUNIT
    [TestFixture(TransactionScopeConcern.NoTransaction, IsolationLevel.Serializable)] // this will always fail! Serializable prevents multiple transation to perform insert queries simultaneously
    [TestFixture(TransactionScopeConcern.NoTransaction, IsolationLevel.ReadCommitted)]
    [TestFixture(TransactionScopeConcern.SuppressAmbientTransaction, IsolationLevel.Serializable)]
    [TestFixture(TransactionScopeConcern.SuppressAmbientTransaction, IsolationLevel.ReadCommitted)]
    [TestFixture(TransactionScopeConcern.EnlistInAmbientTransaction, IsolationLevel.Serializable)] // this will always fail! Serializable prevents multiple transation to perform insert queries simultaneously
    [TestFixture(TransactionScopeConcern.EnlistInAmbientTransaction, IsolationLevel.ReadCommitted)]
#endif
    public class Multiple_Failing_TransactionScopes_When_EnlistInAmbientTransaction_is_and_IsolationLevel_is :
        MultipleConnectionsWithMultipleTransactionScopes
    {
        public Multiple_Failing_TransactionScopes_When_EnlistInAmbientTransaction_is_and_IsolationLevel_is(
            TransactionScopeConcern enlistInAmbientTransaction,
            IsolationLevel transationIsolationLevel
            ) : base(enlistInAmbientTransaction, transationIsolationLevel, completeTransaction: false)
        { }

        [Fact]
        public void should_throw_an_Exception_only_if_enlist_in_ambient_transaction_and_IsolationLevel_is_Serializable()
        {
            if (_enlistInAmbientTransaction != TransactionScopeConcern.SuppressAmbientTransaction
                && _transationIsolationLevel == IsolationLevel.Serializable)
            {
                _thrown.Should().BeOfType<AggregateException>();
                _thrown.InnerException.Should().BeOfType<StorageException>();
                // two serializable transactions on the same connection can result in deadlocks.
                _thrown.InnerException.Message.Should().Contain("deadlock");
            }
            else
            {
                _thrown.Should().BeNull();
            }
        }

        [Fact]
        public void Should_have_expected_number_of_commits()
        {
            _commits = Persistence.GetFrom().ToArray();
            if (_enlistInAmbientTransaction == TransactionScopeConcern.SuppressAmbientTransaction)
            {
                _commits.Length.Should().Be(Loop * StreamsPerTransaction);
            }
            else
            {
                _commits.Length.Should().Be(0);
            }
        }
    }

    public abstract class MultipleConnectionsWithSingleTransactionScope : TransactionConcern
    {
        protected ICommit[] _commits;
        protected const int Loop = 2;
        protected const int StreamsPerTransaction = 20;
        protected readonly IsolationLevel _transationIsolationLevel;
        protected readonly TransactionScopeConcern _enlistInAmbientTransaction;
        protected Exception _thrown;
        protected readonly bool _completeTransaction;

        protected MultipleConnectionsWithSingleTransactionScope(
            TransactionScopeConcern enlistInAmbientTransaction,
            IsolationLevel transationIsolationLevel,
            bool completeTransaction
            )
        {
            _transationIsolationLevel = transationIsolationLevel;
            _enlistInAmbientTransaction = enlistInAmbientTransaction;
            _completeTransaction = completeTransaction;
            Reinitialize(enlistInAmbientTransaction);
        }

        protected override void Because()
        {
            _thrown = Catch.Exception(() =>
            {
                var eventStore = new OptimisticEventStore(Persistence, null);

                using (var scope = new TransactionScope(TransactionScopeOption.Required,
                        new TransactionOptions { IsolationLevel = _transationIsolationLevel }
#if NET451 || NETSTANDARD2_0
                    , TransactionScopeAsyncFlowOption.Enabled
#endif
                ))
                {
                    var res = Parallel.For(0, Loop, i =>
                    {
                        int j;
                        for (j = 0; j < StreamsPerTransaction; j++)
                        {
                            var streamId = i.ToString() + "-" + j.ToString();
                            using (var stream = eventStore.OpenStream(streamId))
                            {
                                for (int k = 0; k < 10; k++)
                                {
                                    stream.Add(new EventMessage { Body = "body" + k });
                                }
                                Debug.WriteLine("Committing Stream: " + streamId);
                                stream.CommitChanges(Guid.NewGuid());
                            }
                        }
                    });
                    Debug.WriteLine("Completing transaction");
                    if (_completeTransaction)
                    {
                        scope.Complete();
                    }
                }
            });
        }
    }

#if MSTEST
[TestClass]
#endif
#if NUNIT
    [TestFixture(TransactionScopeConcern.SuppressAmbientTransaction, IsolationLevel.Serializable)]
    [TestFixture(TransactionScopeConcern.SuppressAmbientTransaction, IsolationLevel.ReadCommitted)]
#endif
    public class Single_Completing_TransactionScope_When_EnlistInAmbientTransaction_is_and_IsolationLevel_is
        : MultipleConnectionsWithSingleTransactionScope
    {
        public Single_Completing_TransactionScope_When_EnlistInAmbientTransaction_is_and_IsolationLevel_is(
            TransactionScopeConcern enlistInAmbientTransaction,
            IsolationLevel transationIsolationLevel
            ) : base(enlistInAmbientTransaction, transationIsolationLevel, completeTransaction: true)
        { }

        [Fact]
        public void should_not_throw_an_Exception()
        {
            _thrown.Should().BeNull();
        }

        [Fact]
        public void Should_have_expected_number_of_commits()
        {
            _commits = Persistence.GetFrom().ToArray();
            _commits.Length.Should().Be(Loop * StreamsPerTransaction);
        }
    }

#if MSTEST
[TestClass]
#endif
#if NUNIT
    [TestFixture(TransactionScopeConcern.SuppressAmbientTransaction, IsolationLevel.Serializable)]
    [TestFixture(TransactionScopeConcern.SuppressAmbientTransaction, IsolationLevel.ReadCommitted)]
#endif
    public class Single_Failing_TransactionScope_When_EnlistInAmbientTransaction_is_and_IsolationLevel_is
        : MultipleConnectionsWithSingleTransactionScope
    {
        public Single_Failing_TransactionScope_When_EnlistInAmbientTransaction_is_and_IsolationLevel_is(
            TransactionScopeConcern enlistInAmbientTransaction,
            IsolationLevel transationIsolationLevel
            ) : base(enlistInAmbientTransaction, transationIsolationLevel, completeTransaction: false)
        { }

        [Fact]
        public void should_not_throw_an_Exception()
        {
            _thrown.Should().BeNull();
        }

        [Fact]
        public void Should_have_expected_number_of_commits()
        {
            _commits = Persistence.GetFrom().ToArray();
            _commits.Length.Should().Be(Loop * StreamsPerTransaction);
        }
    }

#if MSTEST
[TestClass]
#endif
#if NUNIT
    [TestFixture(TransactionScopeConcern.NoTransaction, IsolationLevel.Serializable)]
    [TestFixture(TransactionScopeConcern.NoTransaction, IsolationLevel.ReadCommitted)]
    [TestFixture(TransactionScopeConcern.EnlistInAmbientTransaction, IsolationLevel.Serializable)] // unsupported: This platform does not support distributed transactions
    [TestFixture(TransactionScopeConcern.EnlistInAmbientTransaction, IsolationLevel.ReadCommitted)] // unsupported: This platform does not support distributed transactions
#endif
    public class Unsupported_Single_Completing_TransactionScope_When_EnlistInAmbientTransaction_is_and_IsolationLevel_is
           : MultipleConnectionsWithSingleTransactionScope
    {
        public Unsupported_Single_Completing_TransactionScope_When_EnlistInAmbientTransaction_is_and_IsolationLevel_is(
            TransactionScopeConcern enlistInAmbientTransaction,
            IsolationLevel transationIsolationLevel
            ) : base(enlistInAmbientTransaction, transationIsolationLevel, completeTransaction: true)
        { }

        [Fact]
        public void should_throw_an_StorageUnavailableException()
        {
            _thrown.Should().BeOfType<AggregateException>();
            AggregateException aex = _thrown as AggregateException;
            aex.InnerExceptions
                .Any(e => e.GetType().IsAssignableFrom(typeof(StorageUnavailableException)))
                .Should().BeTrue();

            //var storageExceptions = aex.InnerExceptions
            //    .Where(e => e.GetType().IsAssignableFrom(typeof(StorageUnavailableException)))
            //    .Select(e => e.Message);
            //storageExceptions.Should()
            //    .Match(c =>
            //        c.Contains("This platform does not support distributed transactions.")
            //        || c.Contains("The Promote method returned an invalid value for the distributed transaction.")
            //    );

            //.Contain("This platform does not support distributed transactions.");
            // the following error means the transaction is being promoted to a distributed one, and still not supported
            //    "The Promote method returned an invalid value for the distributed transaction.");
        }
    }
}

#pragma warning restore S101 // Types should be named in PascalCase
#pragma warning restore 169 // ReSharper disable InconsistentNaming
#pragma warning restore IDE1006 // Naming Styles