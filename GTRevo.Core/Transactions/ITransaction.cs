﻿using System;
using System.Threading.Tasks;

namespace GTRevo.Core.Transactions
{
    public interface ITransaction : IDisposable
    {
        void Commit();
        Task CommitAsync();
    }
}
