if (args.Length != 1) throw new ArgumentException("Pass the repository root for the source layout check.");
Tests.Contracts.ContractChecks.Run(args[0]);
