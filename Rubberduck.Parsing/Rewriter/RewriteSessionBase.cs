﻿using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Rubberduck.Parsing.VBA.Parsing;
using Rubberduck.VBEditor;

namespace Rubberduck.Parsing.Rewriter
{
    public abstract class RewriteSessionBase : IRewriteSession
    {
        protected readonly IDictionary<QualifiedModuleName, IExecutableModuleRewriter> CheckedOutModuleRewriters = new Dictionary<QualifiedModuleName, IExecutableModuleRewriter>();
        protected readonly IRewriterProvider RewriterProvider; 

        private readonly Func<IRewriteSession, bool> _rewritingAllowed;

        protected readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly object _invalidationLockObject = new object();

        public abstract CodeKind TargetCodeKind { get; }

        protected RewriteSessionBase(IRewriterProvider rewriterProvider, Func<IRewriteSession, bool> rewritingAllowed)
        {
            RewriterProvider = rewriterProvider;
            _rewritingAllowed = rewritingAllowed;
        }


        public IModuleRewriter CheckOutModuleRewriter(QualifiedModuleName module)
        {
            if (CheckedOutModuleRewriters.TryGetValue(module, out var rewriter))
            {
                return rewriter;
            }
            
            rewriter = ModuleRewriter(module);
            CheckedOutModuleRewriters.Add(module, rewriter);
            return rewriter;
        }

        protected abstract IExecutableModuleRewriter ModuleRewriter(QualifiedModuleName module);

        public void Rewrite()
        {
            if (!CheckedOutModuleRewriters.Any())
            {
                return;
            }

            lock (_invalidationLockObject)
            {
                if (_isInvalidated)
                {
                    Logger.Warn("Tried to execute Rewrite on a RewriteSession that was already invalidated.");
                    return;
                }
            }

            if (!_rewritingAllowed(this))
            {
                Logger.Warn("Tried to execute Rewrite on a RewriteSession when rewriting was no longer allowed.");
                return;
            }

            RewriteInternal();
        }

        protected abstract void RewriteInternal();

        private bool _isInvalidated = false;
        public bool IsInvalidated
        {
            get
            {
                lock (_invalidationLockObject)
                {
                    return _isInvalidated;
                }
            }
        }

        public void Invalidate()
        {
            lock(_invalidationLockObject)
            {
                _isInvalidated = true;
            }
        }
    }
}