# [1.23.0-beta.47](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.46...v1.23.0-beta.47) (2026-09-10)


### Bug Fixes

* NetworkRigidbody origin-aware reworks ([a887b17](https://github.com/PurrNet/PurrNet/commit/a887b17645b21553cac01fd11229993978127bf7))


### Performance Improvements

* editor IL processing improvements ([21e89a9](https://github.com/PurrNet/PurrNet/commit/21e89a903dcdd41a08885d13e25b485a09faadd9))

# [1.23.0-beta.46](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.45...v1.23.0-beta.46) (2026-09-10)


### Bug Fixes

* Editor network prefabs/assets assets staying loaded forever in ([1fa89cb](https://github.com/PurrNet/PurrNet/commit/1fa89cb0b60da993648b0333b4343f4a64023f9b))

# [1.23.0-beta.45](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.44...v1.23.0-beta.45) (2026-09-09)


### Bug Fixes

* keep interned paths internal and reuse owned path buffers ([9fa9f3e](https://github.com/PurrNet/PurrNet/commit/9fa9f3e9fe75856eadce90f253ab854d4e89c2bb))


### Performance Improvements

* drop interned paths when pools are cleared ([ef95034](https://github.com/PurrNet/PurrNet/commit/ef9503457e05769cb6b9de80c6998e3b5dd8b344))
* expose interned nearest parent paths as read only ([3ca5811](https://github.com/PurrNet/PurrNet/commit/3ca5811e6d42089d7586623b842ecd85a95d4e0e))
* intern nearest parent paths to avoid per spawn allocations ([bf5ba3e](https://github.com/PurrNet/PurrNet/commit/bf5ba3e5c9d38a071dead34d1a436a5b5011b870))

# [1.23.0-beta.44](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.43...v1.23.0-beta.44) (2026-09-09)


### Bug Fixes

* guard scene handle hash for Unity 6000.5 ([724db75](https://github.com/PurrNet/PurrNet/commit/724db757728ddb0a167a0a57b461502f4c7181fe))


### Performance Improvements

* avoid boxed enumerator when clearing observers ([af90813](https://github.com/PurrNet/PurrNet/commit/af90813c65e528a8af64fbd62be0ae8f6c1511e1))
* avoid boxed enumerator when despawning pooled hierarchies ([6f8d089](https://github.com/PurrNet/PurrNet/commit/6f8d089d6358748c44eb1186a56ae703483359c7))
* avoid boxing on scene id lookups ([86cc45d](https://github.com/PurrNet/PurrNet/commit/86cc45d7f362ff99e4c0f61f91345c726b997b80))
* avoid closure alloc when clearing failed async observer roots ([b876833](https://github.com/PurrNet/PurrNet/commit/b8768331abba33bf2f8c6ac2bac6125348415440))
* reuse direct children list in PreparePrefabInfo ([657fe19](https://github.com/PurrNet/PurrNet/commit/657fe194f772da4a62863539ac9b99e7bfd08fd8))

# [1.23.0-beta.43](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.42...v1.23.0-beta.43) (2026-09-09)


### Performance Improvements

* dispatch generated module initializers without reflection ([dd1133e](https://github.com/PurrNet/PurrNet/commit/dd1133e6f0702126b53f8270718fc093360b1f61))
* resolve prefab data by lookup instead of scanning ([38b3964](https://github.com/PurrNet/PurrNet/commit/38b3964db1af53401a667a5670918b04f9bd8fe5))

# [1.23.0-beta.42](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.41...v1.23.0-beta.42) (2026-09-09)


### Performance Improvements

* reuse interpolation buffers across respawns ([a5603cc](https://github.com/PurrNet/PurrNet/commit/a5603cc9c33310430473d8b7cc6d3c8dd044b7d7))

# [1.23.0-beta.41](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.40...v1.23.0-beta.41) (2026-09-09)


### Bug Fixes

* IL2CPP function pointer crashes webgl when exceptions are disabled ([1a7dbc3](https://github.com/PurrNet/PurrNet/commit/1a7dbc3692e7f2e83e7a3bf2f7cdd3e3a134f2e5))

# [1.23.0-beta.40](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.39...v1.23.0-beta.40) (2026-09-07)


### Performance Improvements

* udp reliable messages now coalesce while queued ([f046c4c](https://github.com/PurrNet/PurrNet/commit/f046c4c26bf5fab20bc79daf4f3f0788bc7a8ac5))

# [1.23.0-beta.39](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.38...v1.23.0-beta.39) (2026-09-07)


### Bug Fixes

* ensure siblings is populated before assuming ([2cced88](https://github.com/PurrNet/PurrNet/commit/2cced88490cc22d9b109068801d71a1536a753df))

# [1.23.0-beta.38](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.37...v1.23.0-beta.38) (2026-09-06)


### Performance Improvements

* cut spawn churn garbage and per-player finish packets ([2e630ad](https://github.com/PurrNet/PurrNet/commit/2e630ad88c6cae46618b700e8e6d7fe261e4700a))

# [1.23.0-beta.37](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.36...v1.23.0-beta.37) (2026-09-06)


### Bug Fixes

* delta registration bug ([3db7937](https://github.com/PurrNet/PurrNet/commit/3db793787ff3397a78df5cbeef3282a73e9e13e8))


### Performance Improvements

* NetworkTransform per-client replay ([efadd94](https://github.com/PurrNet/PurrNet/commit/efadd94c90c604ca982fe550e5cab09d77047ebd))

# [1.23.0-beta.36](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.35...v1.23.0-beta.36) (2026-09-06)


### Performance Improvements

* introduce a new `OnObserversAdded` with a list of batches; cache visibility results; NetworkTransform uses new observer event for fanout ([0bf65a6](https://github.com/PurrNet/PurrNet/commit/0bf65a68aec2e4a8811f2ff27cc37d13b6e95666))

# [1.23.0-beta.35](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.34...v1.23.0-beta.35) (2026-09-06)


### Bug Fixes

* UDP GC for .netstandard ([6e7668c](https://github.com/PurrNet/PurrNet/commit/6e7668cfd28711da0a6db6d8d868688c03e06e90))

# [1.23.0-beta.34](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.33...v1.23.0-beta.34) (2026-09-06)


### Performance Improvements

* improve visibility and cache sibling components ([550987b](https://github.com/PurrNet/PurrNet/commit/550987bab7d74411ef82208cb70fe47ae6976b54))

# [1.23.0-beta.33](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.32...v1.23.0-beta.33) (2026-09-06)


### Bug Fixes

* revert NT ACK patch, it was not worth it ([32e6053](https://github.com/PurrNet/PurrNet/commit/32e6053cabbd570e8d3105326ce0ffae513a9744))

# [1.23.0-beta.32](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.31...v1.23.0-beta.32) (2026-09-06)


### Bug Fixes

* forgot to push file ([a256909](https://github.com/PurrNet/PurrNet/commit/a2569099aefbac4a7594a29b3853edbe62cc832d))

# [1.23.0-beta.31](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.30...v1.23.0-beta.31) (2026-09-06)


### Bug Fixes

* NT ACK correctness ([2b7fd72](https://github.com/PurrNet/PurrNet/commit/2b7fd72b7e08df530ea0007664d490563e214f3e))

# [1.23.0-beta.30](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.29...v1.23.0-beta.30) (2026-09-05)


### Bug Fixes

* improve NetworkIdentity direct children lookup ([dbb56b7](https://github.com/PurrNet/PurrNet/commit/dbb56b7683f9cece31b3d9d4cd4818b26033fce5))

# [1.23.0-beta.29](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.28...v1.23.0-beta.29) (2026-09-05)


### Bug Fixes

* clear up some GC on the myers diff ([cdb25d0](https://github.com/PurrNet/PurrNet/commit/cdb25d0aec9ed298d86adb8b84e4d79602353ac9))
* fast-path for when direct network children are 0 ([81668bd](https://github.com/PurrNet/PurrNet/commit/81668bd45abec6bb5dce092638c3770416ca84d2))

# [1.23.0-beta.28](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.27...v1.23.0-beta.28) (2026-09-04)


### Bug Fixes

* syncvar bug and tests to ensure we dont regress ([2aea0b7](https://github.com/PurrNet/PurrNet/commit/2aea0b7ddeb940b7a44b8b93e4f39967865dc8d5))

# [1.23.0-beta.27](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.26...v1.23.0-beta.27) (2026-09-04)


### Bug Fixes

* optimize litenetlib batching for single packets ([b83d746](https://github.com/PurrNet/PurrNet/commit/b83d746982bbbc97542811ec07353c62ac1056bb))

# [1.23.0-beta.26](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.25...v1.23.0-beta.26) (2026-09-04)


### Bug Fixes

* improving on the adaptive NT CPU usage ([1200724](https://github.com/PurrNet/PurrNet/commit/12007249941e6f8ab82feb966e7c595253ee18c5))

# [1.23.0-beta.25](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.24...v1.23.0-beta.25) (2026-09-04)


### Bug Fixes

* NetworkTransform registration regression ([5725df0](https://github.com/PurrNet/PurrNet/commit/5725df091f703f3d07fd233b90870ff6eb09d7a0))

# [1.23.0-beta.24](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.23...v1.23.0-beta.24) (2026-09-03)


### Bug Fixes

* transports can now flush per targer ([e982797](https://github.com/PurrNet/PurrNet/commit/e98279774e484a1fa71304d1ebeee1ca144a946f))

# [1.23.0-beta.23](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.22...v1.23.0-beta.23) (2026-09-03)


### Bug Fixes

* frame-accurate RTT; immediate rpcs also send on the same frame ([6ab4fad](https://github.com/PurrNet/PurrNet/commit/6ab4fadd785d2c3f5bd5f70c9b3506623d7fd400))

# [1.23.0-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.21...v1.23.0-beta.22) (2026-09-03)


### Bug Fixes

* ILProcessing for unity 6.6 ([bdbbb82](https://github.com/PurrNet/PurrNet/commit/bdbbb8205c66f8729f707892e201f582a2c74824))
* when using Purrnet, Object.DontDestroyOnLoad() can fail ([2370872](https://github.com/PurrNet/PurrNet/commit/237087254d76d0cafa9f55c40ce1d2368caae0be))

# [1.23.0-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.20...v1.23.0-beta.21) (2026-09-03)


### Bug Fixes

* PlayerSpawner not spawning if DespawnIfOwnerDisconnects = false && ([62b2a7f](https://github.com/PurrNet/PurrNet/commit/62b2a7f7f32bb15580c418a14df2c71134e8945c))

# [1.23.0-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.19...v1.23.0-beta.20) (2026-09-03)


### Bug Fixes

* scene scoped assets test + fixes ([325c740](https://github.com/PurrNet/PurrNet/commit/325c740e945ba7b1206a0509e6c672f9517dd02b))

# [1.23.0-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.18...v1.23.0-beta.19) (2026-09-02)


### Features

* allow a custom prefab resolver for custom resolution of prefabs ([d1f1caa](https://github.com/PurrNet/PurrNet/commit/d1f1caa0374252cfbefd4c026de12a6ed7408c79))
* NetworkScenePrefabs/Assets ([1ab95e6](https://github.com/PurrNet/PurrNet/commit/1ab95e63f0271912241ba0dfe99180b1a54ff6ac))

# [1.23.0-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.17...v1.23.0-beta.18) (2026-09-02)


### Bug Fixes

* Making up for path delay with pings ([e2c160a](https://github.com/PurrNet/PurrNet/commit/e2c160abb995a65e4f34a6e8e8ff822d56ff2c35))

# [1.23.0-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.16...v1.23.0-beta.17) (2026-09-02)


### Bug Fixes

* Pinging fix for Steam ([e6f423e](https://github.com/PurrNet/PurrNet/commit/e6f423e86bab3ebb6c8c30dbe6cc68b99f3873d8))

# [1.23.0-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.15...v1.23.0-beta.16) (2026-09-02)


### Bug Fixes

* Pre-connection pinging support ([538e959](https://github.com/PurrNet/PurrNet/commit/538e959655c3026a3a886d9d0074782ec5c94716))

# [1.23.0-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.14...v1.23.0-beta.15) (2026-09-02)


### Bug Fixes

* Removed PurrChat from toolbar ([ed451d5](https://github.com/PurrNet/PurrNet/commit/ed451d5ce3555df05c2d8386b24df5a981c29e3a))

# [1.23.0-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.13...v1.23.0-beta.14) (2026-09-02)


### Bug Fixes

* reusing shared data instead of re-packing each time ([c0928ba](https://github.com/PurrNet/PurrNet/commit/c0928ba78641efe709c5fefa73e13e5e3eab055b))

# [1.23.0-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.12...v1.23.0-beta.13) (2026-09-01)


### Bug Fixes

* delta module cache ([e6274d7](https://github.com/PurrNet/PurrNet/commit/e6274d77ee4b40798376f1da9d0460536a7d0b3e))
* network transform shared baseline ([33ebf20](https://github.com/PurrNet/PurrNet/commit/33ebf20dce3e0b7200ef79d0aeeafc12ecfc36cc))
* shared baseline for delta module ([c3af09d](https://github.com/PurrNet/PurrNet/commit/c3af09d0567892d6033e3929f47387aad597628f))

# [1.23.0-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.11...v1.23.0-beta.12) (2026-08-31)


### Bug Fixes

* parenting and NT smoothness ([61d2e13](https://github.com/PurrNet/PurrNet/commit/61d2e139b507d8e617b28fee303c9096a5dcc181))

# [1.23.0-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.10...v1.23.0-beta.11) (2026-08-31)


### Bug Fixes

* async spawning reinforcement ([c052897](https://github.com/PurrNet/PurrNet/commit/c052897759acf10287f6b767769a0bd12546668d))

# [1.23.0-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.9...v1.23.0-beta.10) (2026-08-31)


### Features

* allow to pickup network prefabs directly from scene references ([fd3852f](https://github.com/PurrNet/PurrNet/commit/fd3852fc494fc1f8fb677843847c52a16394266b))

# [1.23.0-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.8...v1.23.0-beta.9) (2026-08-31)


### Bug Fixes

* error if parent is changed without permissions + revert to correct ([0871412](https://github.com/PurrNet/PurrNet/commit/08714126db53605d5b0ea29936e536f90d4f5ac8))

# [1.23.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.7...v1.23.0-beta.8) (2026-08-29)


### Bug Fixes

* Async instantiation despawn ordering ([d75571a](https://github.com/PurrNet/PurrNet/commit/d75571a944849ec5a5486b964d3f21ec111c94df))

# [1.23.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.6...v1.23.0-beta.7) (2026-08-29)


### Bug Fixes

* Async instantiation fix ([afc0a60](https://github.com/PurrNet/PurrNet/commit/afc0a608d6c7810fb2b3818f255deb361872a919))

# [1.23.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.5...v1.23.0-beta.6) (2026-08-27)


### Bug Fixes

* improve new updates (perf + gc) ([21e4bfc](https://github.com/PurrNet/PurrNet/commit/21e4bfc1def19e626e08eb4852410398d46564a4))

# [1.23.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.4...v1.23.0-beta.5) (2026-08-27)


### Bug Fixes

* lower GC from NetworkTransform delegates, add a handler and RemoveAt for PurrAction with O(1) removal time ([6368f93](https://github.com/PurrNet/PurrNet/commit/6368f931d10ad6f828cef240f7c81e35e4bc2e72))

# [1.23.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.3...v1.23.0-beta.4) (2026-08-27)


### Bug Fixes

* purrnet package manager installation process ([a266046](https://github.com/PurrNet/PurrNet/commit/a266046d2d5a5e5bf27b41dae4bbf180508cd8a7))

# [1.23.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.2...v1.23.0-beta.3) (2026-08-25)


### Bug Fixes

* unity 2022 bad api ([b716b9c](https://github.com/PurrNet/PurrNet/commit/b716b9ca1da75d43db079271222dacc40018bcee))

# [1.23.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.23.0-beta.1...v1.23.0-beta.2) (2026-08-21)


### Bug Fixes

* move OwnerOnly to be an override of NetworkModule ([58e4984](https://github.com/PurrNet/PurrNet/commit/58e4984dc61b8936701c608ecba3c7ac5174ef82))

# [1.23.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.22.1-beta.15...v1.23.0-beta.1) (2026-08-21)


### Features

* [OwnerOnly] for sync types, this is a common case to not dealing ([6e597de](https://github.com/PurrNet/PurrNet/commit/6e597de06e08b902a189dc1cfba566483a6f9023))

## [1.22.1-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.22.1-beta.14...v1.22.1-beta.15) (2026-08-21)


### Bug Fixes

* Awaitable ServerRpc returning an IPackedSimple struct returns ([9d61f33](https://github.com/PurrNet/PurrNet/commit/9d61f338a02a2467550eeff027e8f3d05e37fc44))

## [1.22.1-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.22.1-beta.13...v1.22.1-beta.14) (2026-08-21)


### Bug Fixes

* SyncEvent bitpacker bug ([5359f7b](https://github.com/PurrNet/PurrNet/commit/5359f7be26e7c7ae7dea566eed285fe3c19982c3))

## [1.22.1-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.22.1-beta.12...v1.22.1-beta.13) (2026-08-20)


### Bug Fixes

* camelCase consistency ([0bd00f1](https://github.com/PurrNet/PurrNet/commit/0bd00f1d09109149b7f36a4ae3cbeb5fccd3a325))

## [1.22.1-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.22.1-beta.11...v1.22.1-beta.12) (2026-08-20)


### Bug Fixes

* cleanup ([cee08b7](https://github.com/PurrNet/PurrNet/commit/cee08b74af42e6345014bc7bdb6c1b1d2d98bff4))
* Performance boost for new PurrAction ([2a80252](https://github.com/PurrNet/PurrNet/commit/2a80252cc80b8567393a021053ad1c3150ac63a1))
* PurrAction guard against null entries ([d489ca5](https://github.com/PurrNet/PurrNet/commit/d489ca56753785dce283289df76f2a9a6bb9d432))

## [1.22.1-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.22.1-beta.10...v1.22.1-beta.11) (2026-08-20)


### Bug Fixes

* SyncBigData resilience and bad config for non "unsafe" network ([dc5ba05](https://github.com/PurrNet/PurrNet/commit/dc5ba051d3dc4f7550048b0839d3e89793f6df70))

## [1.22.1-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.22.1-beta.9...v1.22.1-beta.10) (2026-08-19)


### Bug Fixes

* Timing of instance handler to subsystem registration ([3c0f676](https://github.com/PurrNet/PurrNet/commit/3c0f676b75f33c613576077ae544f1dcb19e7dfc))

## [1.22.1-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.22.1-beta.8...v1.22.1-beta.9) (2026-08-19)


### Bug Fixes

* network animator auto sync also syncs layer weights ([8c4b376](https://github.com/PurrNet/PurrNet/commit/8c4b37661de77246454abb0aa933b9f74760ab2e))

## [1.22.1-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.22.1-beta.7...v1.22.1-beta.8) (2026-08-19)


### Bug Fixes

* Statistics manager improved initial ping & reporting ([cbfabac](https://github.com/PurrNet/PurrNet/commit/cbfabacfb06c28ed82bfbe3b1e40f165d60262c8))

## [1.22.1-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.22.1-beta.6...v1.22.1-beta.7) (2026-08-18)


### Bug Fixes

* PurrNet new icons ([778eba1](https://github.com/PurrNet/PurrNet/commit/778eba12a87ba7d5f5d2d8d0677ffe1dc7268a84))

## [1.22.1-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.22.1-beta.5...v1.22.1-beta.6) (2026-08-18)


### Bug Fixes

* BitPacker duplicate bug ([9daf35f](https://github.com/PurrNet/PurrNet/commit/9daf35fbd7a6655e1d663da536ac67d0aba8cc3d))

## [1.22.1-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.22.1-beta.4...v1.22.1-beta.5) (2026-08-17)


### Bug Fixes

* improved delta packers for other primitive types ([5d1ca99](https://github.com/PurrNet/PurrNet/commit/5d1ca99b5890c7a6653454e7b065ff9290e64491))

## [1.22.1-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.22.1-beta.3...v1.22.1-beta.4) (2026-08-17)


### Bug Fixes

* better float delta packer ([ec0638b](https://github.com/PurrNet/PurrNet/commit/ec0638bd74f6f85a1f11b17b2df0e4bb8407c096))

## [1.22.1-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.22.1-beta.2...v1.22.1-beta.3) (2026-08-14)


### Bug Fixes

* NetworkTransform ownership transfers smoothly transitions instead of teleporting ([b8fafaf](https://github.com/PurrNet/PurrNet/commit/b8fafaf740d12b85690d275b3a625f0f8e053829))

## [1.22.1-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.22.1-beta.1...v1.22.1-beta.2) (2026-08-13)


### Bug Fixes

* scene reload cleanup issue ([762ee79](https://github.com/PurrNet/PurrNet/commit/762ee7987ccfebe7f5ffb5bc1b6efcfd17c43d2d))

## [1.22.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.22.0...v1.22.1-beta.1) (2026-08-13)


### Bug Fixes

* include `com.uniy.mathematics` dep ([375ce4b](https://github.com/PurrNet/PurrNet/commit/375ce4bc50ac8893e7a3cf312c3f2ce2e3f30c14))
* not sure why my unity has something not on the website ([9df82f5](https://github.com/PurrNet/PurrNet/commit/9df82f5d66bc44b1f852787d70a90e82aec265e9))
* typo ([96816eb](https://github.com/PurrNet/PurrNet/commit/96816ebe58c4fff3eaa757cab73778805d8131f9))

## [1.22.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.22.0...v1.22.1-beta.1) (2026-08-13)


### Bug Fixes

* include `com.uniy.mathematics` dep ([375ce4b](https://github.com/PurrNet/PurrNet/commit/375ce4bc50ac8893e7a3cf312c3f2ce2e3f30c14))
* not sure why my unity has something not on the website ([9df82f5](https://github.com/PurrNet/PurrNet/commit/9df82f5d66bc44b1f852787d70a90e82aec265e9))

# [1.22.0](https://github.com/PurrNet/PurrNet/compare/v1.21.1...v1.22.0) (2026-08-11)


### Bug Fixes

* add search bar to the editor packages window ([410b995](https://github.com/PurrNet/PurrNet/commit/410b995c4440d3da98e664c62c797f82cb715c10))
* Addressable scene handling return op ([3e16c03](https://github.com/PurrNet/PurrNet/commit/3e16c034dac1a63c4d145a7ce68b8eb34ec49d8f))
* allow purrnet package manager to import tools as simple unitypackages ([ac84ca7](https://github.com/PurrNet/PurrNet/commit/ac84ca7b0d8391f7ca03cde0c524fb2032183c86))
* allow to see pending updates on the category ([82cb076](https://github.com/PurrNet/PurrNet/commit/82cb07689a2cf866d2b8a1e219ed15c8433b4a2c))
* Async instantiation proper versioning support ([c8adfd8](https://github.com/PurrNet/PurrNet/commit/c8adfd8b8f6037aff459217e99e91bfb0d0410fe))
* Codegen CoreCLR cleanup ([a04ab68](https://github.com/PurrNet/PurrNet/commit/a04ab6878901a0f04c53e65fe3e161856b449913))
* **codegen:** make serializer registration CoreCLR-safe ([857d158](https://github.com/PurrNet/PurrNet/commit/857d15824820b9e9e5cb3ab21a2d45e0d2ebd7d4))
* Duplicate network prefab non spawning issue ([ab1b7c4](https://github.com/PurrNet/PurrNet/commit/ab1b7c48adb99c3aed3057a83e78a58731504213))
* Generate on assets saves properly ([1701013](https://github.com/PurrNet/PurrNet/commit/1701013631660caa3a1d829dd01c16b7a6415510))
* handle late async spawn confirmations ([b23c031](https://github.com/PurrNet/PurrNet/commit/b23c031fbea81ed560f68f7804294f1754dbb1dd))
* hierarchy traversal optimization ([da7dee1](https://github.com/PurrNet/PurrNet/commit/da7dee18b3c22b0a90cbf63ffe21e5888dd9b3dd))
* if animator is disabled don't mess with it's state cause it causes unity warnings to spam ([eb51ca7](https://github.com/PurrNet/PurrNet/commit/eb51ca7e8fc7818609bc7b9d8f7d79457a3974b6))
* improve tick timing accuracy to prevent big jumps ([3bbe66e](https://github.com/PurrNet/PurrNet/commit/3bbe66e043ee3d5fd03457265a7313334021266b))
* Improved eventual consistency of network rigidbody ([23d440a](https://github.com/PurrNet/PurrNet/commit/23d440a60abceb17ce8f3a80bc7c1572c0cfaf04))
* include Nakama transport warning ([4b54eee](https://github.com/PurrNet/PurrNet/commit/4b54eeef0257dbc9fcd9c210b03793d91f1149bd))
* Inspector inconsistency for Odin support ([babe1f8](https://github.com/PurrNet/PurrNet/commit/babe1f8609b2022063482d6be5a1c7568c3925f9))
* Make sync input utilize Immediate data ([08ec4b0](https://github.com/PurrNet/PurrNet/commit/08ec4b0315b66daec987550547133f90cdb57fdd))
* more async instantiation overloads ([606d231](https://github.com/PurrNet/PurrNet/commit/606d23126f8680c47531ae2e924e5bb483a55a80))
* Name clash of network prefabs ([b30c2fe](https://github.com/PurrNet/PurrNet/commit/b30c2feb21da3a4792ef2a8298b422b5a4e4a059))
* network bones, include inactive skinnes mesh renderers to avoid ([da5978d](https://github.com/PurrNet/PurrNet/commit/da5978d8f7bba4be8ee87ad77e1a149c81259185))
* Network transform default to local ([b90d2e7](https://github.com/PurrNet/PurrNet/commit/b90d2e789c7a6f26d2a617ce77db0e4e8d7018ee))
* NetworkAnimator reconcile logic ([68a9d95](https://github.com/PurrNet/PurrNet/commit/68a9d957fc1fb5a67705340e3ab10da1be10c604))
* NetworkAudioSource skipping small clips or starting delayed ([655fc20](https://github.com/PurrNet/PurrNet/commit/655fc20fe7365b483dc2ea8288a24ef7219aea8f))
* Opt out of auto spawning ([f25ce91](https://github.com/PurrNet/PurrNet/commit/f25ce919ab87df8eb9166f678930f866635d605f))
* Optimize allocation of Network Transform ([a977820](https://github.com/PurrNet/PurrNet/commit/a9778207d6f3f538ad9a30570d72490b41b4ceee))
* Player spawner introduced inconsistency ([787320e](https://github.com/PurrNet/PurrNet/commit/787320e92bdf908f903bc917b16af032b111418e))
* PlayerSpawner makes up for opt-out auto spawning ([afd734b](https://github.com/PurrNet/PurrNet/commit/afd734bd1773a02efbeee69994ffc3ae68ae0194))
* Prefab lookup ([b37ccc2](https://github.com/PurrNet/PurrNet/commit/b37ccc265d810b7a269a5771e5fcdfd404b6872f))
* Scene module addressable handling fix ([2f832aa](https://github.com/PurrNet/PurrNet/commit/2f832aaa07927a301cf7af4625d4fe277cb542b8))
* Smoother eventual consistency for network RB ([a4a5fe4](https://github.com/PurrNet/PurrNet/commit/a4a5fe48840eb87f0f2c1f22dbef5c2f9ee4c827))
* Statistics manager packet loss + reconnect bug ([5541f1b](https://github.com/PurrNet/PurrNet/commit/5541f1b12fc1749c527f30c214485b66488edb0c))
* Sync dictionary and list initial packet match check ([8300ef7](https://github.com/PurrNet/PurrNet/commit/8300ef708b608f6817d518386ae331a6860227ac))
* undo value behavior of disposable collections ([ac744fa](https://github.com/PurrNet/PurrNet/commit/ac744fae020357d342b719d288dec2c84aa694a4))
* validate serializer method return types ([e0fddd7](https://github.com/PurrNet/PurrNet/commit/e0fddd7fdf7936f730db30ff7c22cfe4e576553a))


### Features

* async instantiation ([365df09](https://github.com/PurrNet/PurrNet/commit/365df0965b7c51f3211186eaa4fcb2966f2235ed))
* DesyncPolicy for determinism and packet loss improvements ([a5e7c62](https://github.com/PurrNet/PurrNet/commit/a5e7c620c985964c600876044d5518244481ab5c))
* immediate RPCs ([e45d30d](https://github.com/PurrNet/PurrNet/commit/e45d30dd2a5bdea229755f9f2ff7a397415d47a2))

# [1.22.0-beta.23](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.22...v1.22.0-beta.23) (2026-08-10)


### Bug Fixes

* Scene module addressable handling fix ([2f832aa](https://github.com/PurrNet/PurrNet/commit/2f832aaa07927a301cf7af4625d4fe277cb542b8))

# [1.22.0-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.21...v1.22.0-beta.22) (2026-08-10)


### Bug Fixes

* Addressable scene handling return op ([3e16c03](https://github.com/PurrNet/PurrNet/commit/3e16c034dac1a63c4d145a7ce68b8eb34ec49d8f))

# [1.22.0-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.20...v1.22.0-beta.21) (2026-08-07)


### Bug Fixes

* Duplicate network prefab non spawning issue ([ab1b7c4](https://github.com/PurrNet/PurrNet/commit/ab1b7c48adb99c3aed3057a83e78a58731504213))
* Improved eventual consistency of network rigidbody ([23d440a](https://github.com/PurrNet/PurrNet/commit/23d440a60abceb17ce8f3a80bc7c1572c0cfaf04))
* Smoother eventual consistency for network RB ([a4a5fe4](https://github.com/PurrNet/PurrNet/commit/a4a5fe48840eb87f0f2c1f22dbef5c2f9ee4c827))

# [1.22.0-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.19...v1.22.0-beta.20) (2026-08-06)


### Bug Fixes

* Statistics manager packet loss + reconnect bug ([5541f1b](https://github.com/PurrNet/PurrNet/commit/5541f1b12fc1749c527f30c214485b66488edb0c))

# [1.22.0-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.18...v1.22.0-beta.19) (2026-08-04)


### Bug Fixes

* Async instantiation proper versioning support ([c8adfd8](https://github.com/PurrNet/PurrNet/commit/c8adfd8b8f6037aff459217e99e91bfb0d0410fe))
* handle late async spawn confirmations ([b23c031](https://github.com/PurrNet/PurrNet/commit/b23c031fbea81ed560f68f7804294f1754dbb1dd))
* hierarchy traversal optimization ([da7dee1](https://github.com/PurrNet/PurrNet/commit/da7dee18b3c22b0a90cbf63ffe21e5888dd9b3dd))
* more async instantiation overloads ([606d231](https://github.com/PurrNet/PurrNet/commit/606d23126f8680c47531ae2e924e5bb483a55a80))
* Opt out of auto spawning ([f25ce91](https://github.com/PurrNet/PurrNet/commit/f25ce919ab87df8eb9166f678930f866635d605f))
* Player spawner introduced inconsistency ([787320e](https://github.com/PurrNet/PurrNet/commit/787320e92bdf908f903bc917b16af032b111418e))
* PlayerSpawner makes up for opt-out auto spawning ([afd734b](https://github.com/PurrNet/PurrNet/commit/afd734bd1773a02efbeee69994ffc3ae68ae0194))
* validate serializer method return types ([e0fddd7](https://github.com/PurrNet/PurrNet/commit/e0fddd7fdf7936f730db30ff7c22cfe4e576553a))


### Features

* async instantiation ([365df09](https://github.com/PurrNet/PurrNet/commit/365df0965b7c51f3211186eaa4fcb2966f2235ed))

# [1.22.0-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.17...v1.22.0-beta.18) (2026-08-04)


### Bug Fixes

* Prefab lookup ([b37ccc2](https://github.com/PurrNet/PurrNet/commit/b37ccc265d810b7a269a5771e5fcdfd404b6872f))

# [1.22.0-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.16...v1.22.0-beta.17) (2026-08-04)


### Bug Fixes

* Inspector inconsistency for Odin support ([babe1f8](https://github.com/PurrNet/PurrNet/commit/babe1f8609b2022063482d6be5a1c7568c3925f9))

# [1.22.0-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.15...v1.22.0-beta.16) (2026-08-04)


### Bug Fixes

* Name clash of network prefabs ([b30c2fe](https://github.com/PurrNet/PurrNet/commit/b30c2feb21da3a4792ef2a8298b422b5a4e4a059))

# [1.22.0-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.14...v1.22.0-beta.15) (2026-08-03)


### Bug Fixes

* Generate on assets saves properly ([1701013](https://github.com/PurrNet/PurrNet/commit/1701013631660caa3a1d829dd01c16b7a6415510))

# [1.22.0-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.13...v1.22.0-beta.14) (2026-08-03)


### Bug Fixes

* Codegen CoreCLR cleanup ([a04ab68](https://github.com/PurrNet/PurrNet/commit/a04ab6878901a0f04c53e65fe3e161856b449913))
* **codegen:** make serializer registration CoreCLR-safe ([857d158](https://github.com/PurrNet/PurrNet/commit/857d15824820b9e9e5cb3ab21a2d45e0d2ebd7d4))

# [1.22.0-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.12...v1.22.0-beta.13) (2026-08-03)


### Bug Fixes

* Sync dictionary and list initial packet match check ([8300ef7](https://github.com/PurrNet/PurrNet/commit/8300ef708b608f6817d518386ae331a6860227ac))

# [1.22.0-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.11...v1.22.0-beta.12) (2026-08-03)


### Bug Fixes

* Make sync input utilize Immediate data ([08ec4b0](https://github.com/PurrNet/PurrNet/commit/08ec4b0315b66daec987550547133f90cdb57fdd))

# [1.22.0-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.10...v1.22.0-beta.11) (2026-08-01)


### Bug Fixes

* undo value behavior of disposable collections ([ac744fa](https://github.com/PurrNet/PurrNet/commit/ac744fae020357d342b719d288dec2c84aa694a4))

# [1.22.0-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.9...v1.22.0-beta.10) (2026-07-30)


### Bug Fixes

* Optimize allocation of Network Transform ([a977820](https://github.com/PurrNet/PurrNet/commit/a9778207d6f3f538ad9a30570d72490b41b4ceee))

# [1.22.0-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.8...v1.22.0-beta.9) (2026-07-30)


### Bug Fixes

* Network transform default to local ([b90d2e7](https://github.com/PurrNet/PurrNet/commit/b90d2e789c7a6f26d2a617ce77db0e4e8d7018ee))

# [1.22.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.7...v1.22.0-beta.8) (2026-07-30)


### Bug Fixes

* include Nakama transport warning ([4b54eee](https://github.com/PurrNet/PurrNet/commit/4b54eeef0257dbc9fcd9c210b03793d91f1149bd))

# [1.22.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.6...v1.22.0-beta.7) (2026-07-30)


### Bug Fixes

* add search bar to the editor packages window ([410b995](https://github.com/PurrNet/PurrNet/commit/410b995c4440d3da98e664c62c797f82cb715c10))

# [1.22.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.5...v1.22.0-beta.6) (2026-07-30)


### Bug Fixes

* allow to see pending updates on the category ([82cb076](https://github.com/PurrNet/PurrNet/commit/82cb07689a2cf866d2b8a1e219ed15c8433b4a2c))

# [1.22.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.4...v1.22.0-beta.5) (2026-07-30)


### Bug Fixes

* allow purrnet package manager to import tools as simple unitypackages ([ac84ca7](https://github.com/PurrNet/PurrNet/commit/ac84ca7b0d8391f7ca03cde0c524fb2032183c86))

# [1.22.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.3...v1.22.0-beta.4) (2026-07-29)


### Bug Fixes

* NetworkAudioSource skipping small clips or starting delayed ([655fc20](https://github.com/PurrNet/PurrNet/commit/655fc20fe7365b483dc2ea8288a24ef7219aea8f))

# [1.22.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.2...v1.22.0-beta.3) (2026-07-29)


### Bug Fixes

* NetworkAnimator reconcile logic ([68a9d95](https://github.com/PurrNet/PurrNet/commit/68a9d957fc1fb5a67705340e3ab10da1be10c604))

# [1.22.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.22.0-beta.1...v1.22.0-beta.2) (2026-07-29)


### Features

* DesyncPolicy for determinism and packet loss improvements ([a5e7c62](https://github.com/PurrNet/PurrNet/commit/a5e7c620c985964c600876044d5518244481ab5c))

# [1.22.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.21.1...v1.22.0-beta.1) (2026-07-28)


### Bug Fixes

* if animator is disabled don't mess with it's state cause it causes unity warnings to spam ([eb51ca7](https://github.com/PurrNet/PurrNet/commit/eb51ca7e8fc7818609bc7b9d8f7d79457a3974b6))
* improve tick timing accuracy to prevent big jumps ([3bbe66e](https://github.com/PurrNet/PurrNet/commit/3bbe66e043ee3d5fd03457265a7313334021266b))
* network bones, include inactive skinnes mesh renderers to avoid ([da5978d](https://github.com/PurrNet/PurrNet/commit/da5978d8f7bba4be8ee87ad77e1a149c81259185))


### Features

* immediate RPCs ([e45d30d](https://github.com/PurrNet/PurrNet/commit/e45d30dd2a5bdea229755f9f2ff7a397415d47a2))

## [1.21.1](https://github.com/PurrNet/PurrNet/compare/v1.21.0...v1.21.1) (2026-07-28)


### Bug Fixes

* Adaptive sync settings ([6d4429f](https://github.com/PurrNet/PurrNet/commit/6d4429f2518d905de981954dcfb1ffd784613955))
* add NT adaptive dump ([9973d9b](https://github.com/PurrNet/PurrNet/commit/9973d9bea77a0f3c27d7378d92a36ef9d4ad16fa))
* Added network transform strategy scriptable ([2368995](https://github.com/PurrNet/PurrNet/commit/23689956943f9387bc4b7f1dfe943c48b09c6349))
* Default predictive sync strategy ([c148083](https://github.com/PurrNet/PurrNet/commit/c148083d0d21c4eb6b491cafac501e6c6b1bea35))
* ensure rot is normalized ([d60c8ba](https://github.com/PurrNet/PurrNet/commit/d60c8ba32b256c2309e105aca44dfbd3f38a6732))
* improve NT and rigibody behavior ([590069b](https://github.com/PurrNet/PurrNet/commit/590069b738ec4c0bcda276b735ceb32360d29d4e))
* Improved rest state of adaptive sync ([3105841](https://github.com/PurrNet/PurrNet/commit/310584132605f52e27c288beeaf65fc976d3b627))
* introduce `FlushImmediately` to `NetworkAnimator` ([e51071a](https://github.com/PurrNet/PurrNet/commit/e51071a19db2ec948106eb63ab9752b8c6e43f65))
* Less buffer size for adaptive NT ([3b2aa89](https://github.com/PurrNet/PurrNet/commit/3b2aa891116ff5965d889808cbf409512cb07380))
* Much improved adaptive sync adaptation ([74347a1](https://github.com/PurrNet/PurrNet/commit/74347a1948215eecfc4639329b6a63d75c36f4ef))
* Network Rigidbody improvements ([0a17af2](https://github.com/PurrNet/PurrNet/commit/0a17af207c7080fd6b13453efc9d1a368e40504e))
* network rigidbody inconsistencies with soft parenting ([eacac38](https://github.com/PurrNet/PurrNet/commit/eacac388e8ecf92f502bffc31e6c820e9a5de1ff))
* revert back to simpler velocity based method ([00cb6b0](https://github.com/PurrNet/PurrNet/commit/00cb6b0ceba907a8cc669dd1cbff6ddd92e38c56))
* Sender suppression added to Network Transform ([9a01631](https://github.com/PurrNet/PurrNet/commit/9a016318b761d829c19fb65df05f5baf15c04d67))
* simplify NT logic and prefer SetPositionAndRotation when both pos and rot changed ([d96947d](https://github.com/PurrNet/PurrNet/commit/d96947d4f57d6b295ba8b5e2d288d83f1919ec8a))
* Sync NetworkRB settings if changed ([1e495cb](https://github.com/PurrNet/PurrNet/commit/1e495cb928a285dc96e5138be9a2ff3c29efa624))
* try to emulate isKinematic without isKinematic ([1f0d3ad](https://github.com/PurrNet/PurrNet/commit/1f0d3ad49aef75ffc7fe6637005d4ed89b36741e))
* Unity 2023 scene fix ([5cd961c](https://github.com/PurrNet/PurrNet/commit/5cd961cf00b7b61489aa6f942849ac5f24cd4235))

## [1.21.1-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.21.1-beta.11...v1.21.1-beta.12) (2026-07-28)


### Bug Fixes

* Unity 2023 scene fix ([5cd961c](https://github.com/PurrNet/PurrNet/commit/5cd961cf00b7b61489aa6f942849ac5f24cd4235))

## [1.21.1-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.21.1-beta.10...v1.21.1-beta.11) (2026-07-26)


### Bug Fixes

* Sync NetworkRB settings if changed ([1e495cb](https://github.com/PurrNet/PurrNet/commit/1e495cb928a285dc96e5138be9a2ff3c29efa624))

## [1.21.1-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.21.1-beta.9...v1.21.1-beta.10) (2026-07-26)


### Bug Fixes

* Network Rigidbody improvements ([0a17af2](https://github.com/PurrNet/PurrNet/commit/0a17af207c7080fd6b13453efc9d1a368e40504e))

## [1.21.1-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.21.1-beta.8...v1.21.1-beta.9) (2026-07-23)


### Bug Fixes

* Less buffer size for adaptive NT ([3b2aa89](https://github.com/PurrNet/PurrNet/commit/3b2aa891116ff5965d889808cbf409512cb07380))

## [1.21.1-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.21.1-beta.7...v1.21.1-beta.8) (2026-07-23)


### Bug Fixes

* Improved rest state of adaptive sync ([3105841](https://github.com/PurrNet/PurrNet/commit/310584132605f52e27c288beeaf65fc976d3b627))

## [1.21.1-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.21.1-beta.6...v1.21.1-beta.7) (2026-07-23)


### Bug Fixes

* add NT adaptive dump ([9973d9b](https://github.com/PurrNet/PurrNet/commit/9973d9bea77a0f3c27d7378d92a36ef9d4ad16fa))

## [1.21.1-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.21.1-beta.5...v1.21.1-beta.6) (2026-07-23)


### Bug Fixes

* Adaptive sync settings ([6d4429f](https://github.com/PurrNet/PurrNet/commit/6d4429f2518d905de981954dcfb1ffd784613955))

## [1.21.1-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.21.1-beta.4...v1.21.1-beta.5) (2026-07-23)


### Bug Fixes

* Much improved adaptive sync adaptation ([74347a1](https://github.com/PurrNet/PurrNet/commit/74347a1948215eecfc4639329b6a63d75c36f4ef))

## [1.21.1-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.21.1-beta.3...v1.21.1-beta.4) (2026-07-23)


### Bug Fixes

* introduce `FlushImmediately` to `NetworkAnimator` ([e51071a](https://github.com/PurrNet/PurrNet/commit/e51071a19db2ec948106eb63ab9752b8c6e43f65))

## [1.21.1-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.21.1-beta.2...v1.21.1-beta.3) (2026-07-23)


### Bug Fixes

* ensure rot is normalized ([d60c8ba](https://github.com/PurrNet/PurrNet/commit/d60c8ba32b256c2309e105aca44dfbd3f38a6732))
* improve NT and rigibody behavior ([590069b](https://github.com/PurrNet/PurrNet/commit/590069b738ec4c0bcda276b735ceb32360d29d4e))
* revert back to simpler velocity based method ([00cb6b0](https://github.com/PurrNet/PurrNet/commit/00cb6b0ceba907a8cc669dd1cbff6ddd92e38c56))
* simplify NT logic and prefer SetPositionAndRotation when both pos and rot changed ([d96947d](https://github.com/PurrNet/PurrNet/commit/d96947d4f57d6b295ba8b5e2d288d83f1919ec8a))
* try to emulate isKinematic without isKinematic ([1f0d3ad](https://github.com/PurrNet/PurrNet/commit/1f0d3ad49aef75ffc7fe6637005d4ed89b36741e))

## [1.21.1-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.21.1-beta.1...v1.21.1-beta.2) (2026-07-22)


### Bug Fixes

* Added network transform strategy scriptable ([2368995](https://github.com/PurrNet/PurrNet/commit/23689956943f9387bc4b7f1dfe943c48b09c6349))
* Default predictive sync strategy ([c148083](https://github.com/PurrNet/PurrNet/commit/c148083d0d21c4eb6b491cafac501e6c6b1bea35))
* Sender suppression added to Network Transform ([9a01631](https://github.com/PurrNet/PurrNet/commit/9a016318b761d829c19fb65df05f5baf15c04d67))

## [1.21.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.21.0...v1.21.1-beta.1) (2026-07-22)


### Bug Fixes

* network rigidbody inconsistencies with soft parenting ([eacac38](https://github.com/PurrNet/PurrNet/commit/eacac388e8ecf92f502bffc31e6c820e9a5de1ff))

# [1.21.0](https://github.com/PurrNet/PurrNet/compare/v1.20.2...v1.21.0) (2026-07-21)


### Bug Fixes

* collider rollback improvements and incorrect math ([62375c2](https://github.com/PurrNet/PurrNet/commit/62375c2af2246ac5f14a0a3632259868d8d6d3a6))
* collider rollback spatial performance ([37808ac](https://github.com/PurrNet/PurrNet/commit/37808ac7d96dab23c495b5ec74eb8af37f7f3dfa))
* delta module cleanup crew comes too soon ([4032a9e](https://github.com/PurrNet/PurrNet/commit/4032a9e073bd2614c260affdbee70280ee85f071))
* Network Rigidbody safety validation step for extreme values ([f895c2c](https://github.com/PurrNet/PurrNet/commit/f895c2c88bf0ca87480720c55fab50d37e679c63))
* take over old connection if cookies match ([0e9b920](https://github.com/PurrNet/PurrNet/commit/0e9b920859f21157b8f6d62e4d62621506b219ae))


### Features

* collider rollback performance + benchmarks + tests ([5b5410d](https://github.com/PurrNet/PurrNet/commit/5b5410d46565cda221fa23ab4454c9c8315d73ee))
* TryGetClosest for collder rollback ([4709f2c](https://github.com/PurrNet/PurrNet/commit/4709f2ce15284b3685e75c9bba3ceb33bcce21b0))

# [1.21.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.21.0-beta.3...v1.21.0-beta.4) (2026-07-21)


### Bug Fixes

* delta module cleanup crew comes too soon ([4032a9e](https://github.com/PurrNet/PurrNet/commit/4032a9e073bd2614c260affdbee70280ee85f071))

# [1.21.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.21.0-beta.2...v1.21.0-beta.3) (2026-07-21)


### Bug Fixes

* Network Rigidbody safety validation step for extreme values ([f895c2c](https://github.com/PurrNet/PurrNet/commit/f895c2c88bf0ca87480720c55fab50d37e679c63))

# [1.21.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.21.0-beta.1...v1.21.0-beta.2) (2026-07-21)


### Bug Fixes

* take over old connection if cookies match ([0e9b920](https://github.com/PurrNet/PurrNet/commit/0e9b920859f21157b8f6d62e4d62621506b219ae))

# [1.21.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.20.2...v1.21.0-beta.1) (2026-07-20)


### Bug Fixes

* collider rollback improvements and incorrect math ([62375c2](https://github.com/PurrNet/PurrNet/commit/62375c2af2246ac5f14a0a3632259868d8d6d3a6))
* collider rollback spatial performance ([37808ac](https://github.com/PurrNet/PurrNet/commit/37808ac7d96dab23c495b5ec74eb8af37f7f3dfa))


### Features

* collider rollback performance + benchmarks + tests ([5b5410d](https://github.com/PurrNet/PurrNet/commit/5b5410d46565cda221fa23ab4454c9c8315d73ee))
* TryGetClosest for collder rollback ([4709f2c](https://github.com/PurrNet/PurrNet/commit/4709f2ce15284b3685e75c9bba3ceb33bcce21b0))

## [1.20.2](https://github.com/PurrNet/PurrNet/compare/v1.20.1...v1.20.2) (2026-07-20)


### Bug Fixes

* probably safe to remove this workaround now ([d4ea518](https://github.com/PurrNet/PurrNet/commit/d4ea518eca0dbac1430b85fdf1df81f65bc1f9e5))

## [1.20.1](https://github.com/PurrNet/PurrNet/compare/v1.20.0...v1.20.1) (2026-07-20)


### Bug Fixes

* RegisterNetworkType roslyn error converted to warning ([e1ec91f](https://github.com/PurrNet/PurrNet/commit/e1ec91fe8259ab08f587bcd4fa1de1445ba39dbc))

# [1.20.0](https://github.com/PurrNet/PurrNet/compare/v1.19.1...v1.20.0) (2026-07-20)


### Bug Fixes

* 6.3 compatibility ([c8ac86c](https://github.com/PurrNet/PurrNet/commit/c8ac86cf1a93065a95eec76c17cc9d9c41c33ba3))
* accept server cookie and PlayerID always ([b4980cc](https://github.com/PurrNet/PurrNet/commit/b4980cc078ae3e8fef46d93e825e9b25285afc1f))
* add exception handling for player event invocations to avoid breaking connection/disconnection flow ([a9b4e0e](https://github.com/PurrNet/PurrNet/commit/a9b4e0e2592f9ff8e087c4e2b7bc5779b409d7b5))
* add fallback to rebuild asset lookup if bake is missing ([a59cf71](https://github.com/PurrNet/PurrNet/commit/a59cf717f29e24930701ffeb75efa9c3e8635229))
* Add force sync window to network rigidbody ([c7ddedb](https://github.com/PurrNet/PurrNet/commit/c7ddedbb183a7f7b48ac5088373b09ccf9f9b1ae))
* add layer weight actions for non-zero layers in NetAnimatorActionBatch ([e662b6d](https://github.com/PurrNet/PurrNet/commit/e662b6d6a2ecd921f9b175dbfeb2ef0d8ec09ba2))
* Add local syncing to network RB ([8c2faf1](https://github.com/PurrNet/PurrNet/commit/8c2faf12b8b39f4daa9d4e571be99c99cf672251))
* Add network condition change callbacks ([1436536](https://github.com/PurrNet/PurrNet/commit/14365369d12433ad67881f2ac5f15d3b1b0fbd1f))
* Add null safety to hierarchy ([7722f33](https://github.com/PurrNet/PurrNet/commit/7722f3341e416f835ca5e10e531c1c3a6916a387))
* Add prediction factor + more correction settings ([072b368](https://github.com/PurrNet/PurrNet/commit/072b3688d3b5569aecb9fef33e582d91f3fc7335))
* Add reference comparer to Network Assets for IL2CPP ([a44f49d](https://github.com/PurrNet/PurrNet/commit/a44f49d08df727d0751db0c540ef273f646c519d))
* add reset of velocity on hard correction ([8f52be6](https://github.com/PurrNet/PurrNet/commit/8f52be6d3772fd21de97ec5eebe2bce520bae0d7))
* Add server transport startup control to composite transport ([e0c4a0c](https://github.com/PurrNet/PurrNet/commit/e0c4a0ca68620e6a831da702a07619f399a39671))
* added abstract settings override to NetworkRigidbody ([3735323](https://github.com/PurrNet/PurrNet/commit/3735323ece8a6f8507c0c1fc11ea59caa9b990bc))
* Added addressables proxy ([8ddc200](https://github.com/PurrNet/PurrNet/commit/8ddc20058cfa3b13690d5486cbb94d061692c845))
* Added advanced settings for Network Rigidbody ([96dd6fc](https://github.com/PurrNet/PurrNet/commit/96dd6fc954d22b13302741ce061185114745d386))
* Added callbacks for addressable scene loading ([01a55c9](https://github.com/PurrNet/PurrNet/commit/01a55c94424d88ca43626bf8f54731332b720a21))
* Added methods to network reflection ([8ada03c](https://github.com/PurrNet/PurrNet/commit/8ada03c293326820a0f92ce3542a57c0e4e8e3b9))
* Added Network Addressables helper struct ([7ab34ad](https://github.com/PurrNet/PurrNet/commit/7ab34addd2d0f6a737875ed12a3b11f5c2b4b961))
* Added purrlogger support ([07a2cb4](https://github.com/PurrNet/PurrNet/commit/07a2cb41ecb9ea364672b90e94c8d377496ba7fd))
* Added server event for addressable loading on player ([adf6ad9](https://github.com/PurrNet/PurrNet/commit/adf6ad97ddc674176917845065b4ecfab28f554c))
* Addressable scene loading for reconnecting ([120c26e](https://github.com/PurrNet/PurrNet/commit/120c26ec7b32ec64bd22e5fa71060860d6ff7538))
* Addressables editor ([27276b3](https://github.com/PurrNet/PurrNet/commit/27276b39aac9c916981c179ac3f8fd1151994232))
* Addressables proper reporting of load state ([cb55fe2](https://github.com/PurrNet/PurrNet/commit/cb55fe2c50d71f7bc01e1d3b4780b9f56df84a2b))
* Addressables utilize network rules properly ([7047be6](https://github.com/PurrNet/PurrNet/commit/7047be611c22b20770f85e4a5100d570f18c0948))
* Allocation optimization of addressables setup ([78108f1](https://github.com/PurrNet/PurrNet/commit/78108f16a2e4a9ee94fc1932fe7ec2cdbfe89fdb))
* Allow compilation of PurrOnGUI outside editor defines ([c70deb2](https://github.com/PurrNet/PurrNet/commit/c70deb2b514d5b3257f3655548290ffded6488e0))
* allow manual spawn to access OnSerialize/Deserialize path ([e394da5](https://github.com/PurrNet/PurrNet/commit/e394da52786df72c78e3fbba3843bd712e9ea5fc))
* allow NakamaTransport.cs to link to existing match id when starting server ([69a75b2](https://github.com/PurrNet/PurrNet/commit/69a75b2cb0d31f283c9ad2919c31f021ff115f97))
* allow NetworkRigidbody to use the parent for velocity calculations ([7bafd6b](https://github.com/PurrNet/PurrNet/commit/7bafd6b73c0287ea13409cd79ad00a417fc8c421))
* allow PurrTransport to use the new Pipe mode (relay updated) ([2c36c6e](https://github.com/PurrNet/PurrNet/commit/2c36c6ef94eeb97275c37c33debeae556a4f4fdb))
* allow SyncBigData base contructor to shine through :) ([a16891c](https://github.com/PurrNet/PurrNet/commit/a16891cbb0c8304f5571a8f08cc6409481db5503))
* allow to make NetworkRigidbody error relative to the parent ([d28d03f](https://github.com/PurrNet/PurrNet/commit/d28d03fa0c06514f9c7cbce0cf9729cfed0189ad))
* allow to supress auto ownership ([bcafea5](https://github.com/PurrNet/PurrNet/commit/bcafea5cea782cc4b147a93776e86b1d9bc34aa1))
* always ensure application constants loaded ([291863a](https://github.com/PurrNet/PurrNet/commit/291863ab6d58ddaa2499a9abddc14babacb51b6e))
* animation reconcile when someone becomes an observer ([a3a9d92](https://github.com/PurrNet/PurrNet/commit/a3a9d929d694ae99f1f5e02589f34123fd640f01))
* app ID included with steam connection ([f315ce1](https://github.com/PurrNet/PurrNet/commit/f315ce15c081139967d9ee94733a3dc1075dc201))
* async packable inconsistency + tests for it ([b37eee1](https://github.com/PurrNet/PurrNet/commit/b37eee130146989a05120a56930bf00435d246c9))
* Async packing change ([ef12d66](https://github.com/PurrNet/PurrNet/commit/ef12d66eb7348868b06a1912933aca9d63bfd84c))
* async RPCs, add a fail fast path ([365bcda](https://github.com/PurrNet/PurrNet/commit/365bcda32738909f1d892c43c28061848e76401d))
* Avoid ambiguity in steam naming ([b8cce5f](https://github.com/PurrNet/PurrNet/commit/b8cce5f4a6ee46f9f7d793239f59f916fd069fc7))
* avoid disabling everything before the initial spawn ([0ebae28](https://github.com/PurrNet/PurrNet/commit/0ebae28bbff42eeb3d13b5b3ffca22f9639fb282))
* avoid reconstructing the targets list every time an RPC is trying to send, instead send instructions to filter the full list ([cf088db](https://github.com/PurrNet/PurrNet/commit/cf088dbb9efca4b06cd2a64a9a0476f957322fb6))
* avoid spamming editor with patch attempts; only do it for clone editors. ([1912c8d](https://github.com/PurrNet/PurrNet/commit/1912c8d7692d2e8ea15b44a8b63cdfd3bf66b711))
* better naming for internal transport ([55b2690](https://github.com/PurrNet/PurrNet/commit/55b2690f1aaee34fbc01889ffdf345204c812c36))
* BitPacker reinforce reading to avoid reading out of bounds ([84c655c](https://github.com/PurrNet/PurrNet/commit/84c655cbae4ea063232290583d0232670188bac2))
* bobsi shenanigans ([b68b786](https://github.com/PurrNet/PurrNet/commit/b68b78666d42f1c8184f78cdfcc5c4d54e556cc6))
* bring back the cached `LateLateUpdate` delegate for NetworkTransform.cs ([0850c26](https://github.com/PurrNet/PurrNet/commit/0850c2692a83d74cf16e4be156a5fae9318b6499))
* bump version ([69d7880](https://github.com/PurrNet/PurrNet/commit/69d7880dcf6f86872155a9f53137167b52956810))
* cache "has rigidbody" check for `NetworkTransform` ([b0c4081](https://github.com/PurrNet/PurrNet/commit/b0c4081711343520b95053da155af8db3d453473))
* cache the TickModule when subscribing inside the SyncVar and use it for unsubscribing for consistency ([7e14941](https://github.com/PurrNet/PurrNet/commit/7e14941bbd4019f0090e500373a456e97fdfdf0c))
* cap extrapolation and handle state settling ([c7e04c3](https://github.com/PurrNet/PurrNet/commit/c7e04c35a766a3cd174fd8a027d284247e6f65d0))
* Cleanup Network RB kinematic handling ([6a16ffe](https://github.com/PurrNet/PurrNet/commit/6a16ffeaf9024dbd01f1e845c486c944ffacda03))
* Cleanup of Network RB calls to self ([6f20e5a](https://github.com/PurrNet/PurrNet/commit/6f20e5ac945b132536826377afc41d752428eb60))
* clear old values beofre adding them ([f803588](https://github.com/PurrNet/PurrNet/commit/f80358880682ff2eb1a505146fbc4d79fba04394))
* client should not update it's last read state from current data, that just seems wrong ([641d33c](https://github.com/PurrNet/PurrNet/commit/641d33ceb49fbb412d0ae69dc5e4b8749a23c041))
* compare BitData by value (made delta validator fail) ([edfdd58](https://github.com/PurrNet/PurrNet/commit/edfdd58f7be4ad199b394d1c3c538c118797497a))
* compares all package.json entries and picks the shallowest one (shortest prefix) instead of ([f5decec](https://github.com/PurrNet/PurrNet/commit/f5dececb4f5888a4f7fec30a53791adb24ec8e9a))
* composite transport disconnect reason ([63d6c79](https://github.com/PurrNet/PurrNet/commit/63d6c7962d173f3b6559aa9643bc4f7609a46d97))
* Convert network assets to new unified setup ([1e149b2](https://github.com/PurrNet/PurrNet/commit/1e149b2cfcf2697d9b99d958264969ef782c44e9))
* Convert network prefabs + addressables to new unified setup ([cad954a](https://github.com/PurrNet/PurrNet/commit/cad954ab69a927cd70bbbdd5fa9f116563e66f32))
* correct variable name for RPC key retrieval in RPCModule ([6ac9140](https://github.com/PurrNet/PurrNet/commit/6ac914042aa287561d21b649aff42a1c3a3bd199))
* dedicated server state missing for owner auth sync types on collections ([9991f3a](https://github.com/PurrNet/PurrNet/commit/9991f3ab1bb505810ad6a575d2a0bc6ca3ccd83c))
* dedicated server syncvar tests were failing on reconnection ([24ef538](https://github.com/PurrNet/PurrNet/commit/24ef538220b194a9d4bcfc959f7d53fac93f4fd6))
* Default network RB to owner auth ([a4c1095](https://github.com/PurrNet/PurrNet/commit/a4c10957ee01db85c53fd6075d3523c93ba14bae))
* defer NetworkOwnershipToggle refresh to LateUpdate ([cefdcfb](https://github.com/PurrNet/PurrNet/commit/cefdcfb01db807bd509508c01ce89f1fb808b2b8))
* deffensively only add keys when .Add is successful ([9781f6c](https://github.com/PurrNet/PurrNet/commit/9781f6ca55666ce9f46b9f4fcf1e3c54268a9f4d))
* deffer OnDeserialize before any OnEarlySpawn is called to avoid execution order mistakes ([09f35d7](https://github.com/PurrNet/PurrNet/commit/09f35d7b60562ab0180a35fcdbf201c3208688c1))
* define symbols for UTP package ([639ccfa](https://github.com/PurrNet/PurrNet/commit/639ccfa88efba3e29962e8ac15ecc5a10494cd64))
* delay player catchup to once they are marked as having the scene loaded ([01445c0](https://github.com/PurrNet/PurrNet/commit/01445c082b28135d492848a18fb0a1f8dcc40be7))
* Delete Assets/PurrNet/Externals/LiteNetLib/LiteNetLib.csproj.meta ([ad616c9](https://github.com/PurrNet/PurrNet/commit/ad616c92e0d7944c2d3e7134b16b6d3d0f78f04e))
* delete should first move, then attempt to delete due to native libraries or opened files ([0206edc](https://github.com/PurrNet/PurrNet/commit/0206edc213886b5282b18dd2e94be28068bee46b))
* deltaPacked with bufferLast causes issues so throw a compiler error for now atleast ([482305b](https://github.com/PurrNet/PurrNet/commit/482305b8379c6fe093e82c797a843a5a61df6e77))
* discard deferred refreshes after despawn ([5459931](https://github.com/PurrNet/PurrNet/commit/5459931576898e51b5453a107f9787e360ca0c02))
* DisposableList ToString null exception ([ed7de11](https://github.com/PurrNet/PurrNet/commit/ed7de11fc7b760487ee987688ba1c7f43e9f81bf))
* do memcmp for unmanaged types when checking equality ([fea1133](https://github.com/PurrNet/PurrNet/commit/fea1133386b821cbe99330aa614de17389e1731d))
* don't delay spawn packet by one tick ([bdbcd52](https://github.com/PurrNet/PurrNet/commit/bdbcd5215a2a7094c95489620d3e269bf7c06196))
* don't force sync to host's own player ([8d95b01](https://github.com/PurrNet/PurrNet/commit/8d95b0100379fdb3c2c85db675e33a4e6845768c))
* don't override NT's pos when no valid data is present (yet) ([88360a0](https://github.com/PurrNet/PurrNet/commit/88360a07436e0b19985f635d3a3ac77ddf7375ee))
* don't print public user IPs in the inspector (NAT debug view) ([413231e](https://github.com/PurrNet/PurrNet/commit/413231e257a70521bcb55aa42d48e95dc2e4fa07))
* don't walk the parent path if you are the one spawning ([1535247](https://github.com/PurrNet/PurrNet/commit/15352470a9acfb664f5e4e9f1c42c78a000fd443))
* dont queue RPCs on migration ([5477dc2](https://github.com/PurrNet/PurrNet/commit/5477dc2a227974f617ce2135368fcb05c59a27cb))
* dont save PurrNet version in the ApplicationConstants.json to avoid git changes for not much value ([f59651f](https://github.com/PurrNet/PurrNet/commit/f59651f37eab736cf4b0fd5c97168270a984c795))
* dont use BitPacker at the FragmentationLayer due to corrupting send array (it is freed on send and net "Get" risks getting the same array back and modifying it). ([187866b](https://github.com/PurrNet/PurrNet/commit/187866b8301c76a5443e62c5ce71c89b7f835989))
* dont use build index for scenes, use path hash instead ([66dfbf0](https://github.com/PurrNet/PurrNet/commit/66dfbf064dde0ef5e9624be9a2d67aeb4b0d1279))
* Editor in 6.5 different callbacks ([761c34e](https://github.com/PurrNet/PurrNet/commit/761c34eb51c92b03e57c92068cc22ed7bbad7ad5))
* enhance bone info and delta module with key hash caching for improved performance ([ab264f0](https://github.com/PurrNet/PurrNet/commit/ab264f0417ffe00a390c10ec64347f5f9cc2a4ad))
* ensure proper unsubscription in CompositeTransport ([32db46a](https://github.com/PurrNet/PurrNet/commit/32db46a1414daf629aa1fd0c10d899c31109330a))
* Ensure rigidbody getter ([1a58f57](https://github.com/PurrNet/PurrNet/commit/1a58f57aa75997a157a79d3c73d6756461ac0258))
* ensure SyncVar old timing, new events actually seem to allow for this in a cleaner more backwards compatible way funnily enough ([5ea249b](https://github.com/PurrNet/PurrNet/commit/5ea249b1749e9ee75b2426250309cc20435983e8))
* exit code for tests ([44915ff](https://github.com/PurrNet/PurrNet/commit/44915ffa08eebabc96efae57730f70df953edce3))
* external git commit hash issue with the package manager ([ad51f09](https://github.com/PurrNet/PurrNet/commit/ad51f095b9f1a6c0315d19d8b6f500c62c41183c))
* FieldAccessException for generic classes with NetworkModules under inheritance ([c648c35](https://github.com/PurrNet/PurrNet/commit/c648c350f168766b38e611392eee339e65077c2f))
* first-time buffering path doesn't respect the BitData's bitOrigin ([bd99a9d](https://github.com/PurrNet/PurrNet/commit/bd99a9daab9200c2316acfdfef7f0175d6f74c68))
* fix some timing issues with OnObserverAdded and packets being split across multiple ticks ([fe50eb8](https://github.com/PurrNet/PurrNet/commit/fe50eb8296ec7021d7fbffa1fd75f20fb32e639f))
* Fragmentation corruption fix ([0f12c24](https://github.com/PurrNet/PurrNet/commit/0f12c24f93cda1334c58fbd650dfbd9e7bb31b9e))
* GC pass across PurrNet ([de4a8c8](https://github.com/PurrNet/PurrNet/commit/de4a8c8b8b04e848a007de314b5a3a726aedc685))
* Generic RPCs fixes and improvemets + tests ([c40665c](https://github.com/PurrNet/PurrNet/commit/c40665c07f3ad58902d735473bf8d7a09e8b897f))
* GetFullPrototype wasn't including all children just the first one for all gameobjects ([926ee40](https://github.com/PurrNet/PurrNet/commit/926ee401655df0bbfbc79edd6f642ed7da95798e))
* Getter for pending addressable scene operations ([377e11d](https://github.com/PurrNet/PurrNet/commit/377e11d639ac50bcf72fbdf1e253b1dc4e141209))
* handle operation cancellation in transport connection logic (silence log) ([eb7ecce](https://github.com/PurrNet/PurrNet/commit/eb7eccec845326631ce409734a36d036026161b7))
* handle player reconnection more gracefully in PlayersManager ([e0ad5b6](https://github.com/PurrNet/PurrNet/commit/e0ad5b686daa4631f245e9a450d122fa0805f935))
* Helper for addressable loading ([46087e2](https://github.com/PurrNet/PurrNet/commit/46087e25b9adee4f26478abcf6de441100df0d84))
* host logic for Nakama ([02e66d7](https://github.com/PurrNet/PurrNet/commit/02e66d78aa01fb6395227789edaa018f5206b1e0))
* host-loopback issue for async RPCs ([56e8ad3](https://github.com/PurrNet/PurrNet/commit/56e8ad336894bf0127e9a5900f52681604ce461b))
* icons failing to be reimported in newer versions ([fa971b8](https://github.com/PurrNet/PurrNet/commit/fa971b8668b14289fdb24aed2a016b272a018611))
* if _raw_rules.Count is 0 it never adds the rule ([c884cfb](https://github.com/PurrNet/PurrNet/commit/c884cfb21f7bf5ef65b5a41f7f889271d9898809))
* IL codegen for async RPCs ([8532f95](https://github.com/PurrNet/PurrNet/commit/8532f9570a55755ca33a4df76cc4abaf23cae1ee))
* IL processing self references security ([4ed7e5d](https://github.com/PurrNet/PurrNet/commit/4ed7e5dee308ef96502edf28a053eae323215aea))
* Implement anonymous telemetry ([9ede7cf](https://github.com/PurrNet/PurrNet/commit/9ede7cf3e0804a9cae556cbd2a1f5d643d16bba7))
* implement IPurrEquatable interface and related equality helpers (to avoid breaking packages that rely on default c# equality) ([9b748c7](https://github.com/PurrNet/PurrNet/commit/9b748c7dd0a7f7d8cdb536b0edbec0802b9b960e))
* Implemented target smoothing for Network RB ([00c7afd](https://github.com/PurrNet/PurrNet/commit/00c7afdeaae8186c44acad010333ef2a3f93d02a))
* impove Equality check for `NetworkTransformData` ([1f0a118](https://github.com/PurrNet/PurrNet/commit/1f0a118e9f26dcf8266b5696a8749a9eaf59e9a0))
* improve batching ([a2322b0](https://github.com/PurrNet/PurrNet/commit/a2322b06368619d73404582822f9f8638c3a2158))
* improve connection telemetry timing ([29ffe65](https://github.com/PurrNet/PurrNet/commit/29ffe6557ce00dd882fca902cb685bd08565bb6c))
* Improve host consistency for Network Rigidbody ([6bfef4b](https://github.com/PurrNet/PurrNet/commit/6bfef4b44e6d4f50b35b6fc91a70a5f37c92c49c))
* Improve nested traversal of Asset scanners ([71e32df](https://github.com/PurrNet/PurrNet/commit/71e32dfeecbcba62651cbf3c761cc721aaa60a9a))
* improve ownership handling to prevent stale snapshots during observer updates ([2ddefa2](https://github.com/PurrNet/PurrNet/commit/2ddefa2e3dc818ab4b75f0bb94e7ce1d4211a6e7))
* improve RPC batching performance ([50493b0](https://github.com/PurrNet/PurrNet/commit/50493b07122a8a88adcb7c5949e02a067ddfea2c))
* improve some UDP delta timings and no need to recycle since AutoRecycle is enabled ([8027850](https://github.com/PurrNet/PurrNet/commit/80278508cee23fb591e52d7b4fad01fcf639af6f))
* Improved addressable network prefabs design unification ([d2c0b01](https://github.com/PurrNet/PurrNet/commit/d2c0b01546df3679c56f76e0bd74d59a449ae1c0))
* Improved asset editors ([0a48f07](https://github.com/PurrNet/PurrNet/commit/0a48f075e74ed81a2c9d1300acc46e0ce1ad5a33))
* Improved client interpolation of network rigidbody ([7038ce5](https://github.com/PurrNet/PurrNet/commit/7038ce58c0a9fb818d05055c852bf085da177e66))
* Improved network reflection method handling ([c2128bd](https://github.com/PurrNet/PurrNet/commit/c2128bdf26fee22834af9430c689c15195c7a61b))
* Improved NetworkRB Support for Unity 5 & 6 ([79b09c8](https://github.com/PurrNet/PurrNet/commit/79b09c8bf8a851ebfb0cda8f2296f02401dc8be2))
* Improved performance of UnifiedAssetPostprocessor ([6454bec](https://github.com/PurrNet/PurrNet/commit/6454becf5b7d5dd489db38e2982cdbe047ec972f))
* Improved telemetry for builds ([594d198](https://github.com/PurrNet/PurrNet/commit/594d198bdf0a4287e9849c00e4b66ae4757e8637))
* include `udpPortV2` in RelayServer and obsolete the old `udpPort` ([f655009](https://github.com/PurrNet/PurrNet/commit/f655009df270b2fb394233dce54da99bb227c65a))
* include chidlren for scene objects ([5cbcbf2](https://github.com/PurrNet/PurrNet/commit/5cbcbf2f6e6ef10616988b86558a5615dd84bd3b))
* include UniTask in the type discovery ([439f992](https://github.com/PurrNet/PurrNet/commit/439f9929b4f934042bd1159d2167ffd155855a8e))
* inconsistent rules check with async RPCs and RequireServer rule ([2dc72af](https://github.com/PurrNet/PurrNet/commit/2dc72af26adfa491bfc7d40bb4ddc80b4b42f122))
* increase MTU margin for delta module ([ceb45c7](https://github.com/PurrNet/PurrNet/commit/ceb45c78378a6f535ab9c218903f007165c829ea))
* inheritance and NetworkModules ([e3c145a](https://github.com/PurrNet/PurrNet/commit/e3c145a78cfacfe82d49b71b70579240b1aaed95))
* Instantiate with InstantiateParameters ([20b8c9d](https://github.com/PurrNet/PurrNet/commit/20b8c9dc557d1d525d54d9e97bd3c4ea4f956281))
* interpolation saturation ([3c3147c](https://github.com/PurrNet/PurrNet/commit/3c3147c7542669b3776fff873fec4b758a34f69a))
* interpolation saturation ([ec045ce](https://github.com/PurrNet/PurrNet/commit/ec045ce569e7ca304694cde31a894b2c5b0ea3ee))
* introcude pre/post scene unloaded events ([8c15a55](https://github.com/PurrNet/PurrNet/commit/8c15a55b5746125d8a676da10dab0320b6718b1a))
* introduce onTeleportCorrection event to NetworkRigidbody.cs ([7bb6457](https://github.com/PurrNet/PurrNet/commit/7bb645752c1d2753c7df830cdfc77f204559b2be))
* introduce some roslyn IDE warnings/errors for quick feedback ([d99e0da](https://github.com/PurrNet/PurrNet/commit/d99e0da50931a0fa3f1647c9555fd613f4870ae1))
* invoke InternalTick in NetworkIdentity when client is not registered ([c164ee0](https://github.com/PurrNet/PurrNet/commit/c164ee011c8c548496a9dedef0be4f0f1473930e))
* lambda RPC not being resolved properly ([362c044](https://github.com/PurrNet/PurrNet/commit/362c044061ac2c2010102bec8d0aedb279bfc9f5))
* LiteNetLib dont cache the available count ([40b44a3](https://github.com/PurrNet/PurrNet/commit/40b44a32be344f91adda783cd2bee6286bf372a1))
* LiteNetLib use safe path for all systems ([73b3493](https://github.com/PurrNet/PurrNet/commit/73b3493a5bad9ed3fab6985c766d5a01cac106d8))
* loopback for async RPC exceptions ([3365282](https://github.com/PurrNet/PurrNet/commit/3365282488743c2ec706e8def8734b1bcadf0424))
* Made RB sleep optional ([653fbda](https://github.com/PurrNet/PurrNet/commit/653fbdad0b30187ef75581da228ec0732dee84a5))
* Make asset processing more efficient ([7a9112a](https://github.com/PurrNet/PurrNet/commit/7a9112af285cf45333b6ed32ac5c0fa56860ad54))
* make auth denial reason nullable ([10c701f](https://github.com/PurrNet/PurrNet/commit/10c701f44350ff2d4d15bd21cff8055bf993c8d6))
* make RigidbodyStateData encode it's position frame to avoid race conditions on the remotes ([7394471](https://github.com/PurrNet/PurrNet/commit/739447198bf15c49001d9c34088696f761c3ab8d))
* make sure GameObjectPrototype doesn't fail with disposed list ([3ab5832](https://github.com/PurrNet/PurrNet/commit/3ab5832c340aba402b2b9c4106704f5078919b4d))
* make sure NetworkRigidbody is fully spawned before we correct position and stuff ([415c061](https://github.com/PurrNet/PurrNet/commit/415c061b7e54b552a5459345d73f2bed18618e85))
* make the masterServer of PurrTransport.cs editable through a property ([4b609da](https://github.com/PurrNet/PurrNet/commit/4b609da14be11ba8d7559eaf4dda907fff5189a9))
* mathematic assembly not being loaded ([bbbbf7d](https://github.com/PurrNet/PurrNet/commit/bbbbf7d2fcc28bdbca5e7bd4335d4ed77488e44c))
* missing meta file for LICENSE.txt ([0ca58c1](https://github.com/PurrNet/PurrNet/commit/0ca58c1dc6b83e0f4c512842a9b0f878d788c87f))
* Missing using directive ([f2a6720](https://github.com/PurrNet/PurrNet/commit/f2a6720542b089c7b295afdda7a67d123c782053))
* misuage protection for disposable list ([83a9086](https://github.com/PurrNet/PurrNet/commit/83a908637babd3c6bf5faf070c0c11cc79b50e96))
* More helpers for addressables loading ([b278abc](https://github.com/PurrNet/PurrNet/commit/b278abc6aac6cfc299a45541b77d425d77560061))
* more ordering issues and better logging ([7b1b44b](https://github.com/PurrNet/PurrNet/commit/7b1b44b71b48c5568ff8fc7c16b3e9056fad684c))
* more resilience to _keys being corrupted ([9376e04](https://github.com/PurrNet/PurrNet/commit/9376e0420fc2ce7650ad6649efe04af2e26285ce))
* Move away from GetInstanceID for asset handling ([763b9fb](https://github.com/PurrNet/PurrNet/commit/763b9fb5a42333e1783b6e1444c3ed4500a7ff6b))
* move mathematics packer to it's own assembly to avoid having others to reference it ([71a23a0](https://github.com/PurrNet/PurrNet/commit/71a23a014111150f64455339dfe82e71a7ba62fd))
* Move PurrTelemetry to internal ([830510c](https://github.com/PurrNet/PurrNet/commit/830510cc1272c3fa359b22a0a4f2c1c48d7a92a3))
* Move to entity ID for newer versions that support it ([b833696](https://github.com/PurrNet/PurrNet/commit/b83369690f4daa24843ba3e5fbe4f35794b55a01))
* Move to singular dispatch OnGUI ([c787c49](https://github.com/PurrNet/PurrNet/commit/c787c49fb5f3588bc923f368a4dfd0035eda9ffa))
* Nakama transport ([f68a4ea](https://github.com/PurrNet/PurrNet/commit/f68a4eaa9d6034d1aba15ed2d343adb7827a39a2))
* Network RB handoff on ownership change ([184d1b9](https://github.com/PurrNet/PurrNet/commit/184d1b9bd23eb83922cd0bcac9cec7a7e9e37d72))
* Network RB initial settings strip delta packing ([4d08208](https://github.com/PurrNet/PurrNet/commit/4d082084989d7370894f4cabfa5e94c63bdb7c9b))
* Network RB late join stuff ([68b2172](https://github.com/PurrNet/PurrNet/commit/68b217205df1d6673d42a42e62d8bf0d671ef283))
* Network RB safety for runtime destroyed RB ([9d151d2](https://github.com/PurrNet/PurrNet/commit/9d151d295f9fd7940296e55a28aedbbe1e63febd))
* Network rigidbody local space scale issue ([2c9ed3b](https://github.com/PurrNet/PurrNet/commit/2c9ed3b59c45c852dca5248595e08f6874b06cbb))
* Network Rigidbody override settings factory instance ([3263f81](https://github.com/PurrNet/PurrNet/commit/3263f814c1107b16b34b0f22538565d6ee327baf))
* Network Rigidbody snapshot fix for server only ([7afa796](https://github.com/PurrNet/PurrNet/commit/7afa7962309a4b3e1174c53e01906c45653411be))
* Network rigidbody stops acting when disabled ([a44a742](https://github.com/PurrNet/PurrNet/commit/a44a74220313f4cd1872702cd523e8a34c65d736))
* Network transform RB issues ([45da2f8](https://github.com/PurrNet/PurrNet/commit/45da2f81524aa05d220326d175ced3097a1eed10))
* **network-transform:** harden unreliable rework ([d5963da](https://github.com/PurrNet/PurrNet/commit/d5963da95658eabf43b3dfdd0dd41a88cc99ec52))
* NetworkAnimator improvements and missing methods ([3b6774a](https://github.com/PurrNet/PurrNet/commit/3b6774a82f7128530f8ec1c1e4d75a20a35a4bde))
* NetworkRigidbody allow to tp locally ([81c6bf4](https://github.com/PurrNet/PurrNet/commit/81c6bf4ecfb85e02bb7f2ed339c478214c1f4b7f))
* NetworkRigidbody handoff ([63ee82b](https://github.com/PurrNet/PurrNet/commit/63ee82be3d21fd273bc0005bb974002af938136b))
* NetworkRigidbody OnGUI strip from builds ([ed4cff9](https://github.com/PurrNet/PurrNet/commit/ed4cff9de70e5dc5ec5e49491c5e66c87a23e832))
* NetworkRigidbody only sync details if there is actually a rigidbody component ([f9ebafe](https://github.com/PurrNet/PurrNet/commit/f9ebafeb9d38fd7740fe91052cc12fdeac0d8bd5))
* NetworkRigidbody ring buffer rework ([b55cd60](https://github.com/PurrNet/PurrNet/commit/b55cd60137e250568bc9bfa5569b2dc95779108f))
* NetworkRigidbody teleport rigidbody left state and the interpolation buffer stale ([20e1331](https://github.com/PurrNet/PurrNet/commit/20e133151f7bb98418a69e9d6975a95a419ce4f7))
* NetworkRigidbody.cs soft parent inconsistencies and added some inspector debugging ([d992bc3](https://github.com/PurrNet/PurrNet/commit/d992bc39dafa6eb98a450daaebd1b0651f997a02))
* NetworkTransform performance improvements ([481d6ec](https://github.com/PurrNet/PurrNet/commit/481d6ecbd8a5c02c742cd24760758d958524245b))
* NetworkTransform.cs prefer the smoother OnEnable version ([c744009](https://github.com/PurrNet/PurrNet/commit/c744009e40de38825ec3a3a891952d4b24709d47))
* new events being too early were causing issues ([5391544](https://github.com/PurrNet/PurrNet/commit/5391544cf00f161c9421c490e11d98da81cd8e68))
* New potential approach to network assets ([be46b14](https://github.com/PurrNet/PurrNet/commit/be46b145806fc5045f485b6520d7f7e153c3d2c6))
* NRE resilience when despawning ([a8b788d](https://github.com/PurrNet/PurrNet/commit/a8b788d2abe608fc6f78d8e7d0de08ceae5d3c35))
* NT and NR interpolation issues ([031b6f1](https://github.com/PurrNet/PurrNet/commit/031b6f19c86c555a1741d2c38424d7c37eadafc6))
* observer addition for spawner ([ede2fe3](https://github.com/PurrNet/PurrNet/commit/ede2fe365d9bde7d197d34d918fae46c0f591b89))
* observers not updating `_latestData` properly ([65dd9d4](https://github.com/PurrNet/PurrNet/commit/65dd9d4937ecc73c34e87279a114565cea3cfe09))
* only install changed files with Purr Packages ([069037c](https://github.com/PurrNet/PurrNet/commit/069037c87cf1246e29fc9b78ee90c3b70a9b380a))
* optimize owner connection checks and caching in NetworkBones ([436f332](https://github.com/PurrNet/PurrNet/commit/436f33271b2eef290a09bd9e395771cb5bc06355))
* ownership callbacks order ([2b1dff1](https://github.com/PurrNet/PurrNet/commit/2b1dff128e44fa241717385243a97375497560fb))
* Ownership missing identity safety ([f6f9232](https://github.com/PurrNet/PurrNet/commit/f6f923235f517dc7675a6089ae02a42f26c45ce4))
* package manager compiler error and extra UI updates ([6b930b3](https://github.com/PurrNet/PurrNet/commit/6b930b36698986fe7bb9daac199d08e5ed7e5a58))
* Package manager shortcut + prefer git urls ([bdf2fcb](https://github.com/PurrNet/PurrNet/commit/bdf2fcbd5b97adad784a2622a71650b30b69188f))
* packer improvements (cpu), and some tests (benchmarks) ([63ca673](https://github.com/PurrNet/PurrNet/commit/63ca673ad01985e683788137fac5ebd5cbbdb8c7))
* parent change flush rpcs before sending packet ([432def8](https://github.com/PurrNet/PurrNet/commit/432def8b0657118a99f157c99217b76516d82613))
* Parent syncing added to Network Rigidbody ([c4a0a89](https://github.com/PurrNet/PurrNet/commit/c4a0a890b4ce862248941f7d902c3d5ba835df5b))
* Parrelsync clone reflection fix ([14cc0aa](https://github.com/PurrNet/PurrNet/commit/14cc0aab68e9cef082d52505e36dbcdecd3f157c))
* patch some catchup logic, still not ideal need to revisit ([511d120](https://github.com/PurrNet/PurrNet/commit/511d12029aa62a069e2a2e34c0b387f9a5e5229f))
* Performance optimizations for Network Rigidbody ([f2be755](https://github.com/PurrNet/PurrNet/commit/f2be75513816b8779bec762346b713e69214d692))
* player leave event; roslyn analyser miss-fires ([860845e](https://github.com/PurrNet/PurrNet/commit/860845ef717be770e3911570b7d00553ea492522))
* player reconnects during host promotion ([00c7bfe](https://github.com/PurrNet/PurrNet/commit/00c7bfee493808c147f3ad6c0f110472114a55d9))
* pooling bugs ([d3c99ea](https://github.com/PurrNet/PurrNet/commit/d3c99ea9b204cb24602446e0fe2fab93eebaa3af))
* pooling bugs ([22eef29](https://github.com/PurrNet/PurrNet/commit/22eef2930c13c802ef2be1baa0364ed301e6dfd0))
* position driver with local coords ([d0e8309](https://github.com/PurrNet/PurrNet/commit/d0e830911aa5ec6cc70c871d5e921bcdaa6f17f8))
* Potential addressables spawning fix ([0de22ac](https://github.com/PurrNet/PurrNet/commit/0de22ac008deef3a747f6a405ee47eea4b1cfc28))
* potential collision fix ([f5f346a](https://github.com/PurrNet/PurrNet/commit/f5f346ab1cd63caa5dc55a373178b92fc5e92075))
* Potential error when generating assets during playmode or compilation ([85b39f8](https://github.com/PurrNet/PurrNet/commit/85b39f82e6a4299adeba9d1608cf93ed31f7b460))
* prefer syncvar naming for SyncLazyRef ([5fe9837](https://github.com/PurrNet/PurrNet/commit/5fe983797efe2505b082fe02fb6be6b62d49e25b))
* preserve sequencing across fragmentation ([b014465](https://github.com/PurrNet/PurrNet/commit/b0144651dd7f5e47367edcbed7f13cc18a3b0219))
* process ManualAddObserver events immediately for SyncVar parity ([9ba4759](https://github.com/PurrNet/PurrNet/commit/9ba475912f8eef60bd7fcd1d97d2a8778e4133fe))
* Processor change for IL2 build issues ([fdf4f53](https://github.com/PurrNet/PurrNet/commit/fdf4f534e6fa5e532c8925960bf30c15992a2156))
* proper cleanup when something is destroyed ([c216d35](https://github.com/PurrNet/PurrNet/commit/c216d35bbab123110063238741c448b96f83fd97))
* Proper serialization of custom struct and classes ([249da35](https://github.com/PurrNet/PurrNet/commit/249da35a6ceb3d6d2097882a72b2ed719bdf5ffa))
* properly copy when delta packing lists ([9f2c2a5](https://github.com/PurrNet/PurrNet/commit/9f2c2a5329aff8ae25f18e59e3f813fbb07c6f8e))
* purr package manager cleanup patches ([64565ac](https://github.com/PurrNet/PurrNet/commit/64565ac70a474dc1fd64e6a44310bb54651f8920))
* PurrNet packages window improvements ([ea1db92](https://github.com/PurrNet/PurrNet/commit/ea1db92c35699c60f1b4aaee82b3f5b7725bc28b))
* PurrNet Packages: allow to update all packages at once ([ecd3291](https://github.com/PurrNet/PurrNet/commit/ecd32918d4a2b36c7d3f84b8e242f0e1d3c5d318))
* PurrNet Packages: Implement user authentication and profile management ([02089cd](https://github.com/PurrNet/PurrNet/commit/02089cd937d10148962345903e1c9d82c795281f))
* PurrTransport NAT bugs and editor rendering ([cb25abb](https://github.com/PurrNet/PurrNet/commit/cb25abbc44952b27a98c7a82f23dff1108629018))
* Push for version change ([8bdc25f](https://github.com/PurrNet/PurrNet/commit/8bdc25f5c71ee154f94403e3558aada5985207b9))
* quick patch to SyncList/SyncDic/SyncArray to early exit from the OnTick event; ideally it should follow the SyncVar patern though ([19ce4b8](https://github.com/PurrNet/PurrNet/commit/19ce4b8d4c69a580be4ff0e146db544cff23a640))
* RB issue ([e58dc40](https://github.com/PurrNet/PurrNet/commit/e58dc40cdf50367d37726e705447f212d362305f))
* re-introduce the safety measures around scene switching, unity is volatile here ([46433f2](https://github.com/PurrNet/PurrNet/commit/46433f2663cd28dee4fad82ec7b1bd47988aaff2))
* reapply ownership toggle state after spawn ([e412c65](https://github.com/PurrNet/PurrNet/commit/e412c65b68804dcce0b19558e52a0e43b02c3ef8))
* Rebuild ([042fd1a](https://github.com/PurrNet/PurrNet/commit/042fd1a87d507fef7da532077045f236dd42b919))
* reconnect ([f934dd9](https://github.com/PurrNet/PurrNet/commit/f934dd97066bcbed4432e6c8ac164c6f713b6d87))
* redundant interpolation patches in GatherState from the NetworkTransform.cs ([e47ccad](https://github.com/PurrNet/PurrNet/commit/e47ccadad5d377b06c3012bf52bcaa39f4df7f27))
* refactor `currentTransport` to match previous behavior ([0814dd8](https://github.com/PurrNet/PurrNet/commit/0814dd82b2a4554b65e2053846a4dda263a0dfe7))
* refactor how and when moving scenes happens when spawning objects ([fa1224b](https://github.com/PurrNet/PurrNet/commit/fa1224b2805a90259e6fab9d672e943a66a505b8))
* refactor package installation process to improve clarity and efficiency ([2c8b3bd](https://github.com/PurrNet/PurrNet/commit/2c8b3bd0e11747d5723d509880301d8e9e9a6809))
* refactoring and fixing async RPC issues ([6957af5](https://github.com/PurrNet/PurrNet/commit/6957af5f1e3e3d3de131d9c37cc47d0cc3a43783))
* register delta serializers with NativeDeltaPacker in IL codegen ([e1ad40b](https://github.com/PurrNet/PurrNet/commit/e1ad40b2c751a17b3f36eeffc7ff1e4517dc4da3))
* remove compression for the delta packet, mixed with delta compression it tends to create bigger packets due to high entropy already ([052a222](https://github.com/PurrNet/PurrNet/commit/052a222249662eb45fcff4965a43f39c5ccd0bad))
* remove proxy transport layer for now ([ebaac4b](https://github.com/PurrNet/PurrNet/commit/ebaac4bb07064801e2d2a04c548c778235b1f124))
* remove redundant code in HierarchyV2.cs ([6bad369](https://github.com/PurrNet/PurrNet/commit/6bad36937154710ecd980992fb8bfb6a9a70cbdf))
* remove redundant empty response sending in RPC error handling ([692af16](https://github.com/PurrNet/PurrNet/commit/692af167bbf05ac51675b6d1f98f7f41c83443e6))
* remove TargetRpc fast path since it breaks ordering ([c701535](https://github.com/PurrNet/PurrNet/commit/c70153563a21ce3548a98d0332c496521ad5b5d4))
* rename namespace for PoolingConfigDrawer to reflect it's editor only ([434bb86](https://github.com/PurrNet/PurrNet/commit/434bb86d26bd5caa286e2c5d2c19b06ac87e6073))
* rename PurrNet Package Manager to PurrNet Packages and fix some bugs ([a346469](https://github.com/PurrNet/PurrNet/commit/a34646958f183ab4a693226d3d10057766461d40))
* replace previous LiteNetLib code with just Unsafe.WriteUnaligned ([28164b7](https://github.com/PurrNet/PurrNet/commit/28164b7a9a00a98afc2a61e939d6f6cb5e0d9a2d))
* resolve absolute reference before OnObserverAdded events ([0ddeb8d](https://github.com/PurrNet/PurrNet/commit/0ddeb8da48ed24d9725064bde7d48f651aaaa9f7))
* respet `DontSyncHashes` for non auto events ([59b55d5](https://github.com/PurrNet/PurrNet/commit/59b55d5a01b755fd6aa9adafbf5c84d20f8dd2bd))
* rework DisposableHashSet, it's packers and add some tests to verify future changes ([334bc47](https://github.com/PurrNet/PurrNet/commit/334bc47a2ca7f8ab37afef3edd41bd70f9a86025))
* reworking some early LOD stuff ([200db77](https://github.com/PurrNet/PurrNet/commit/200db77361976fb6febeeb47221558e5c0718bd4))
* SafeRemoveDirectory(folderPath) now runs before CleanupLegacyPackageFiles ([9a99877](https://github.com/PurrNet/PurrNet/commit/9a9987724b22cdc37da033fba190a9ac2a113df2))
* Safety for network identity inspector ([0a794eb](https://github.com/PurrNet/PurrNet/commit/0a794eb229a815b6ec6d3c901cf6b3288af020a2))
* same for static RPCs ([0a0b8eb](https://github.com/PurrNet/PurrNet/commit/0a0b8eb08a72c190334f9572d24a807e5b40153e))
* scene and syncvar bug ([9cd9732](https://github.com/PurrNet/PurrNet/commit/9cd973256e7478b37c1cb1f2a1ad82dfd6142a41))
* Searching added to asset management ([571c30d](https://github.com/PurrNet/PurrNet/commit/571c30dad31890a49c427dd79f9278f5c0915373))
* senders serialized customData breadth-first while the receiver deserialized it depth-first. ([29e5daa](https://github.com/PurrNet/PurrNet/commit/29e5daa93a69b94deeef8d2ea9ae50edc7933237))
* shortcut loops and other expenssive lookups early if Statistics.shouldTrack is false ([e6fb515](https://github.com/PurrNet/PurrNet/commit/e6fb515a1f9b73192110e70b11e8faec64cd4807))
* skip unspawned network identities when building game object framework ([25f04a7](https://github.com/PurrNet/PurrNet/commit/25f04a7d484040961a53a4775b3f534402595896))
* snapshot jumping with world shifting on NR ([c406040](https://github.com/PurrNet/PurrNet/commit/c406040a6fab9c14e31222167864ff27d4d81daa))
* some more disposable collections copying edgecases ([c6efbe8](https://github.com/PurrNet/PurrNet/commit/c6efbe8d146b5ab2ae66bab8761b30a5db0bf491))
* stale NetworkTransform.cs data when re-enabling component ([5332464](https://github.com/PurrNet/PurrNet/commit/53324645257f1c854c0a1915fb398f625317379d))
* State machine dedicated server state handling ([7659953](https://github.com/PurrNet/PurrNet/commit/7659953f6802dff9a906a8eab930dc20c527ee0d))
* State machine race condition ([6cdd8e7](https://github.com/PurrNet/PurrNet/commit/6cdd8e7ba7117b7519088e42f6b6baad89b202ca))
* StateMachine insertion ([048c557](https://github.com/PurrNet/PurrNet/commit/048c557cb3e2cb527fa3c4901d7161459d495b07))
* Statistics manager consistency improvements ([471c569](https://github.com/PurrNet/PurrNet/commit/471c56919c2b69d45a1ebd8d6171fe8a2495c091))
* Statistics manager consistency issues ([bbf883c](https://github.com/PurrNet/PurrNet/commit/bbf883c869788a496ca39c9dd83595826254e21a))
* Statistics manager GUI for builds ([8e28039](https://github.com/PurrNet/PurrNet/commit/8e28039595d0780670e25d666f342389da0044db))
* Stop correction range from utilizing velocity damping ([eff1b2b](https://github.com/PurrNet/PurrNet/commit/eff1b2b5bc95a727dce9a27021ecc4d1056db24e))
* Stop requiring RB on the NetworkRB ([1758f88](https://github.com/PurrNet/PurrNet/commit/1758f882097c41f3e419f5fabbbec5276dd623c8))
* store a client owner version for correct OnOwnerChanged callbacks in host mode ([a12293b](https://github.com/PurrNet/PurrNet/commit/a12293bbccf925c6f30632ad909ba208e37055ca))
* streamline SafeRemoveDirectory implementation for improved clarity and efficiency ([90f3200](https://github.com/PurrNet/PurrNet/commit/90f320085c55bac0da9cc76494bafd74492d69ee))
* suppress TargetRpc first parameter not being used warning ([1354374](https://github.com/PurrNet/PurrNet/commit/1354374ff228e00ca4632ec7c6576c44059b3d6a))
* supress some warnings for NakamaTransport.cs when it isnt installed ([29c554a](https://github.com/PurrNet/PurrNet/commit/29c554af847a2cc53b34f504723ca2fd264e9029))
* Sync array ownership fixes ([04f2f3d](https://github.com/PurrNet/PurrNet/commit/04f2f3dcf61b7633883fe5aaf69352c0c6584149))
* synchashset initial state ([c129afa](https://github.com/PurrNet/PurrNet/commit/c129afa6501425110db95de64026773309e5790e))
* SyncVar _isDirty hand-off bug ([1da4117](https://github.com/PurrNet/PurrNet/commit/1da411752d1a9fbb510786136b946ed364ae3d97))
* syncvar force sync; more pooling behavior ([c0db18e](https://github.com/PurrNet/PurrNet/commit/c0db18ec21b38522f1d3f0fd4059fd8e0f5b9649))
* syncvar issue + tests ([cba8532](https://github.com/PurrNet/PurrNet/commit/cba85326268297df4bae80e3342b0f27f97fa9b0))
* SyncVar reconnect sync issues ([c6d9cbb](https://github.com/PurrNet/PurrNet/commit/c6d9cbbdbc22e9ee392b1b863584a1dc52440d46))
* Take project ID to purrversion json ([140cee2](https://github.com/PurrNet/PurrNet/commit/140cee244b76092cd0ac5efc36a76a0941ed6e78))
* TargetRPC bufferLast was not recording per target but globally ([bd58bb6](https://github.com/PurrNet/PurrNet/commit/bd58bb623d3d1fb74c808b3ba5887757e205aabb))
* targetrpc host scenarios, asyncpackable host scenarios ([5bfb9c8](https://github.com/PurrNet/PurrNet/commit/5bfb9c8285d2c22320e11e67b478a1875d7045ea))
* Telemetry project ID improved handling ([49e52bf](https://github.com/PurrNet/PurrNet/commit/49e52bf2e127a8eb5b038f5b4b0471b3856ce64e))
* teleport should... teleport ([c7e40e8](https://github.com/PurrNet/PurrNet/commit/c7e40e8d2031ba0b66f5441c5c782526b7079d6c))
* timing issues with NT ([ed46035](https://github.com/PurrNet/PurrNet/commit/ed4603510798b8bafc4754ee7b84784c964432eb))
* trigger server callbacks on the correct module (async packables need it) ([0ab9f5e](https://github.com/PurrNet/PurrNet/commit/0ab9f5e4d771a4ebc943efe445f6d6105cc2819d))
* Try to avoid missing scripts causing editor spam ([d36ef20](https://github.com/PurrNet/PurrNet/commit/d36ef206b246f395fe5eaeeddb7817193eb83f3b))
* undo previous change regarding TargetRPC routing when HOST ([4a4fe88](https://github.com/PurrNet/PurrNet/commit/4a4fe88c69436ace522a8481bab0d7accd0b55a0))
* undo rawTransport breaking change ([a066ff3](https://github.com/PurrNet/PurrNet/commit/a066ff3888deebd0f5d257187f20ec01580d8c44))
* update bone entry size calculations for accurate MTU handling in NetworkBones ([9ae2cde](https://github.com/PurrNet/PurrNet/commit/9ae2cde5ba0d019569afe0f8e8dd085dd83a9253))
* update docs base URL ([406e30f](https://github.com/PurrNet/PurrNet/commit/406e30f822c574f9fb96e00a65220214316855d3))
* update LiteNetLib ([7f1f348](https://github.com/PurrNet/PurrNet/commit/7f1f3488ab0d97573dae7673bc333bbe6a02ee74))
* update litenetlib on relay too ([16071d4](https://github.com/PurrNet/PurrNet/commit/16071d4c68d16b9bfdf1bbae34222ae6b5640b48))
* update LiteNetLib to latest (2.1.2) ([1db1f76](https://github.com/PurrNet/PurrNet/commit/1db1f766426c2f75ee46f24c2c299ea40aa766fe))
* update MTU retrieval logic for server and client transports ([8f837a4](https://github.com/PurrNet/PurrNet/commit/8f837a4ac6bf265135cceb5f0ce6e684c10693ee))
* update MTU retrieval logic in PlayersManager and optimize byte length calculation in RPCBatch ([e35d7b4](https://github.com/PurrNet/PurrNet/commit/e35d7b42a8de9e2a91665ab0e3cb7dfb42bed804))
* update NetworkTransform.cs _cachedIsController when OnOwnerDisconnected ([b67aa48](https://github.com/PurrNet/PurrNet/commit/b67aa48ebdd82a2dd79df09d6ee584841f93977e))
* update purrnet packages with bug fixes and performance improvements ([43cce1a](https://github.com/PurrNet/PurrNet/commit/43cce1a374d7bd39c448272cb997d8508f8107ac))
* use manager local player (server would check for default...) ([a306011](https://github.com/PurrNet/PurrNet/commit/a30601138cd8835a3381a4f704e6925ea8e6c235))
* use OnSerialize/OnDeserialize for syncvars ([2c15f29](https://github.com/PurrNet/PurrNet/commit/2c15f297f5e7ce32d4ac8d3b039acf9614ca9065))
* use our enumerator for DisposableDictionary for determinism reasons ([7dda783](https://github.com/PurrNet/PurrNet/commit/7dda783e522f2d6e986172daf624b07154ccddc1))
* use Packages folder instead such that clones respect it, also dont include version in the folder name ([9185c9c](https://github.com/PurrNet/PurrNet/commit/9185c9c423bc325c8d7129ea90f9656e56bac2b5))
* useDelta for single TargetRpc ([ce695f7](https://github.com/PurrNet/PurrNet/commit/ce695f729934653be38806fd2f340d4dfd8687f4))
* Utilize caching for network reflection prefabs ([ecbf92d](https://github.com/PurrNet/PurrNet/commit/ecbf92d315910950e64a4ab51821dc25db3d11ed))
* Utilize collection index for asset management sorting ([6195823](https://github.com/PurrNet/PurrNet/commit/6195823cadea7a869784b2cd6f051440156ab266))
* Utilize type caching avoiding async packable boxing ([5264cf7](https://github.com/PurrNet/PurrNet/commit/5264cf74cd6d27c593dc3334b9773c54611844da))
* Utilize ValueTask to avoid GC on synchronous completion ([7e552f2](https://github.com/PurrNet/PurrNet/commit/7e552f22f9c53e6b9ea11578091f5395bb4fef07))
* UTP server odd symbols ([1991e0c](https://github.com/PurrNet/PurrNet/commit/1991e0cd0cf99193b64456ae75831299ec05a9ce))
* Validated syncvar can now be server authored as well ([1f3de10](https://github.com/PurrNet/PurrNet/commit/1f3de10be3f9c89e212640007ce4b713b35ebc81))
* Validated syncvar old value inconsistency ([6473bf1](https://github.com/PurrNet/PurrNet/commit/6473bf1c2167edc6e5280b2c2c4bfdaba88ee698))
* Visibility safeguard for null ([ce62c74](https://github.com/PurrNet/PurrNet/commit/ce62c7419da14050c37cf63825be2e7d6c39d266))
* Warning for Unity namespace ([75cc2f0](https://github.com/PurrNet/PurrNet/commit/75cc2f0756c41021f27891fa051ed486334966b3))
* what an idiot... i need to sleep man; networktransform fixes ([9335560](https://github.com/PurrNet/PurrNet/commit/9335560002b1bd68c8b7011320a59d4e503b7766))


### Features

* add _authenticator setter ([96d975f](https://github.com/PurrNet/PurrNet/commit/96d975fee284229f1f58452535c44d0b3b8e4923))
* add GlobalNetworkID and SyncLazyRef classes for lazy network identity synchronization ([aed8940](https://github.com/PurrNet/PurrNet/commit/aed8940cf47c936bcf3e774dcda90904c498dd47))
* add nakama transport ([730ef5e](https://github.com/PurrNet/PurrNet/commit/730ef5e918747429791c8939c215c9261c2bce01))
* add network simulation settings for UDP transport (LiteNetLib specific) ([ceed9dd](https://github.com/PurrNet/PurrNet/commit/ceed9dd18d3f3c99495a3662575e44848fd0b490))
* add support for runtime animator controller and avatar in NetAnimator ([97524cf](https://github.com/PurrNet/PurrNet/commit/97524cf47ac1ce6eb95371522706273d3c49c3e9))
* add variant of AuthenticationBehaviour that can give a deny response to client ([b0edd85](https://github.com/PurrNet/PurrNet/commit/b0edd85c4329f0eb9b71dc67499fefe5fabe14d3))
* allow position transform similar to networkrigidbody transform but for NetworkTransform.cs ([4a06b8e](https://github.com/PurrNet/PurrNet/commit/4a06b8e100b62a5b0cefa290862c8095abd9237a))
* allow RPCs to override MTU exceeded behavior ([6717f92](https://github.com/PurrNet/PurrNet/commit/6717f929bbee9923fbfcf8b1ef370ab8827cfc4a))
* allow to discover instantiated network identities based on NetworkRules ([ce50f45](https://github.com/PurrNet/PurrNet/commit/ce50f459dbc22c0373702d21a7b4198f76c0e4f0))
* ApplicationConstants ([b62c62d](https://github.com/PurrNet/PurrNet/commit/b62c62d68782316bbf27e1c8fe577a3ce47758b7))
* async RPC fail fast on target disconnect + GC improvements ([d3d28f5](https://github.com/PurrNet/PurrNet/commit/d3d28f551febf2618f273d9c632277c61dfa0436))
* better diagnosis logs for missmatching types and for RPC exceptions ([92470e3](https://github.com/PurrNet/PurrNet/commit/92470e33ac1fd1274ff4b3958456318473fecb0c))
* cache `hasConnectedOwner` (expenssive getter) ([b54bc66](https://github.com/PurrNet/PurrNet/commit/b54bc66d3229ca09d09ee19d8cb943ecc6f7ff9c))
* configurable MTU exceeded behaviour for unreliable channels ([1f58143](https://github.com/PurrNet/PurrNet/commit/1f581437e6c8ea9f26580a591ec2f5ce57bd1c89))
* enhance authentication version mismatch handling with configurable behavior ([48d222e](https://github.com/PurrNet/PurrNet/commit/48d222e9859275557b56eb9a9d28e382deb00649))
* events that allow cheaper disposable collection packing ([5d94039](https://github.com/PurrNet/PurrNet/commit/5d94039fd9e2f121ee4cfed4a842654e79a9eb16))
* fragment unreliable packets ([e36dbb6](https://github.com/PurrNet/PurrNet/commit/e36dbb66e16d05f95768ac2ba24eb1962cdaac43))
* implement global control for auto start flags in NetworkManager ([745525e](https://github.com/PurrNet/PurrNet/commit/745525e600a9d7fa021e848fc95df2adaf6fc46f))
* improved fragmenation + profiler dropped stats ([050c9c4](https://github.com/PurrNet/PurrNet/commit/050c9c41ef4667b479ccacd78c69e7562d7d1860))
* introduce a hash resolver window to debug unregistered IDs and resolve them to their type ([09901d5](https://github.com/PurrNet/PurrNet/commit/09901d5b0ddc826674a3bbd37e0fe95e5983bff2))
* introduce a thin layer between purrnet and the transport for some finer control ([b26c76c](https://github.com/PurrNet/PurrNet/commit/b26c76c39a0c4aab477dba173fb9e5e9e7b3568a))
* introduce OnSpawnerFlush, complements OnSerialize for both client and server spawning ([9551f65](https://github.com/PurrNet/PurrNet/commit/9551f65cf04ec234cfae8ed44fbc8d10355fe6e0))
* IPersistentPrefabProvider and PersistentId ([85b2d18](https://github.com/PurrNet/PurrNet/commit/85b2d18ee5c661bbe7cfee589a013fc16502248e))
* NAT p2p support for PurrTransport ([b8ee129](https://github.com/PurrNet/PurrNet/commit/b8ee12926c1db6de013d643027082b3118764e3c))
* NetworkRigidbody.cs position driver ([da26c4f](https://github.com/PurrNet/PurrNet/commit/da26c4fea1e3484009d0886f9604c8f73714a894))
* NetworkRigidbody.cs soft parenting ([c38ea9b](https://github.com/PurrNet/PurrNet/commit/c38ea9b12326142e686818a6b8d81b71f75cfc8c))
* NetworkTransform unreliable rework ([fb0f6d5](https://github.com/PurrNet/PurrNet/commit/fb0f6d5315ec0cee5386a44f108d5c045fca2eda))
* OnSerialize/OnDeserialize for spawn coupled data ([cf93d09](https://github.com/PurrNet/PurrNet/commit/cf93d09c0c6eb47c788e97e944878d3bd9792b3f))


### Performance Improvements

* **network-transform:** reduce ack and queue overhead ([37518b6](https://github.com/PurrNet/PurrNet/commit/37518b62e332e4d128c4751ce84e6497715d9ece))

# [1.20.0-beta.270](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.269...v1.20.0-beta.270) (2026-07-20)


### Bug Fixes

* 6.3 compatibility ([c8ac86c](https://github.com/PurrNet/PurrNet/commit/c8ac86cf1a93065a95eec76c17cc9d9c41c33ba3))

# [1.20.0-beta.269](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.268...v1.20.0-beta.269) (2026-07-20)


### Bug Fixes

* suppress TargetRpc first parameter not being used warning ([1354374](https://github.com/PurrNet/PurrNet/commit/1354374ff228e00ca4632ec7c6576c44059b3d6a))

# [1.20.0-beta.268](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.267...v1.20.0-beta.268) (2026-07-20)


### Bug Fixes

* misuage protection for disposable list ([83a9086](https://github.com/PurrNet/PurrNet/commit/83a908637babd3c6bf5faf070c0c11cc79b50e96))


### Features

* events that allow cheaper disposable collection packing ([5d94039](https://github.com/PurrNet/PurrNet/commit/5d94039fd9e2f121ee4cfed4a842654e79a9eb16))
* improved fragmenation + profiler dropped stats ([050c9c4](https://github.com/PurrNet/PurrNet/commit/050c9c41ef4667b479ccacd78c69e7562d7d1860))

# [1.20.0-beta.267](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.266...v1.20.0-beta.267) (2026-07-19)


### Features

* allow RPCs to override MTU exceeded behavior ([6717f92](https://github.com/PurrNet/PurrNet/commit/6717f929bbee9923fbfcf8b1ef370ab8827cfc4a))

# [1.20.0-beta.266](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.265...v1.20.0-beta.266) (2026-07-19)


### Bug Fixes

* interpolation saturation ([3c3147c](https://github.com/PurrNet/PurrNet/commit/3c3147c7542669b3776fff873fec4b758a34f69a))
* interpolation saturation ([ec045ce](https://github.com/PurrNet/PurrNet/commit/ec045ce569e7ca304694cde31a894b2c5b0ea3ee))

# [1.20.0-beta.265](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.264...v1.20.0-beta.265) (2026-07-19)


### Bug Fixes

* don't walk the parent path if you are the one spawning ([1535247](https://github.com/PurrNet/PurrNet/commit/15352470a9acfb664f5e4e9f1c42c78a000fd443))

# [1.20.0-beta.264](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.263...v1.20.0-beta.264) (2026-07-19)


### Bug Fixes

* compare BitData by value (made delta validator fail) ([edfdd58](https://github.com/PurrNet/PurrNet/commit/edfdd58f7be4ad199b394d1c3c538c118797497a))

# [1.20.0-beta.263](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.262...v1.20.0-beta.263) (2026-07-17)


### Bug Fixes

* Potential error when generating assets during playmode or compilation ([85b39f8](https://github.com/PurrNet/PurrNet/commit/85b39f82e6a4299adeba9d1608cf93ed31f7b460))

# [1.20.0-beta.262](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.261...v1.20.0-beta.262) (2026-07-16)


### Bug Fixes

* ensure SyncVar old timing, new events actually seem to allow for this in a cleaner more backwards compatible way funnily enough ([5ea249b](https://github.com/PurrNet/PurrNet/commit/5ea249b1749e9ee75b2426250309cc20435983e8))

# [1.20.0-beta.261](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.260...v1.20.0-beta.261) (2026-07-16)


### Bug Fixes

* new events being too early were causing issues ([5391544](https://github.com/PurrNet/PurrNet/commit/5391544cf00f161c9421c490e11d98da81cd8e68))

# [1.20.0-beta.260](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.259...v1.20.0-beta.260) (2026-07-16)


### Features

* introduce OnSpawnerFlush, complements OnSerialize for both client and server spawning ([9551f65](https://github.com/PurrNet/PurrNet/commit/9551f65cf04ec234cfae8ed44fbc8d10355fe6e0))

# [1.20.0-beta.259](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.258...v1.20.0-beta.259) (2026-07-16)


### Bug Fixes

* use OnSerialize/OnDeserialize for syncvars ([2c15f29](https://github.com/PurrNet/PurrNet/commit/2c15f297f5e7ce32d4ac8d3b039acf9614ca9065))

# [1.20.0-beta.258](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.257...v1.20.0-beta.258) (2026-07-16)


### Bug Fixes

* player leave event; roslyn analyser miss-fires ([860845e](https://github.com/PurrNet/PurrNet/commit/860845ef717be770e3911570b7d00553ea492522))

# [1.20.0-beta.257](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.256...v1.20.0-beta.257) (2026-07-11)


### Bug Fixes

* preserve sequencing across fragmentation ([b014465](https://github.com/PurrNet/PurrNet/commit/b0144651dd7f5e47367edcbed7f13cc18a3b0219))

# [1.20.0-beta.256](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.255...v1.20.0-beta.256) (2026-07-11)


### Features

* fragment unreliable packets ([e36dbb6](https://github.com/PurrNet/PurrNet/commit/e36dbb66e16d05f95768ac2ba24eb1962cdaac43))

# [1.20.0-beta.255](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.254...v1.20.0-beta.255) (2026-07-11)


### Bug Fixes

* improve batching ([a2322b0](https://github.com/PurrNet/PurrNet/commit/a2322b06368619d73404582822f9f8638c3a2158))
* improve RPC batching performance ([50493b0](https://github.com/PurrNet/PurrNet/commit/50493b07122a8a88adcb7c5949e02a067ddfea2c))

# [1.20.0-beta.254](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.253...v1.20.0-beta.254) (2026-07-10)


### Bug Fixes

* **network-transform:** harden unreliable rework ([d5963da](https://github.com/PurrNet/PurrNet/commit/d5963da95658eabf43b3dfdd0dd41a88cc99ec52))


### Features

* NetworkTransform unreliable rework ([fb0f6d5](https://github.com/PurrNet/PurrNet/commit/fb0f6d5315ec0cee5386a44f108d5c045fca2eda))


### Performance Improvements

* **network-transform:** reduce ack and queue overhead ([37518b6](https://github.com/PurrNet/PurrNet/commit/37518b62e332e4d128c4751ce84e6497715d9ece))

# [1.20.0-beta.253](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.252...v1.20.0-beta.253) (2026-07-07)


### Bug Fixes

* Addressables editor ([27276b3](https://github.com/PurrNet/PurrNet/commit/27276b39aac9c916981c179ac3f8fd1151994232))

# [1.20.0-beta.252](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.251...v1.20.0-beta.252) (2026-07-07)


### Bug Fixes

* Network transform RB issues ([45da2f8](https://github.com/PurrNet/PurrNet/commit/45da2f81524aa05d220326d175ced3097a1eed10))

# [1.20.0-beta.251](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.250...v1.20.0-beta.251) (2026-07-07)


### Bug Fixes

* Addressables utilize network rules properly ([7047be6](https://github.com/PurrNet/PurrNet/commit/7047be611c22b20770f85e4a5100d570f18c0948))

# [1.20.0-beta.250](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.249...v1.20.0-beta.250) (2026-07-07)


### Bug Fixes

* Addressables proper reporting of load state ([cb55fe2](https://github.com/PurrNet/PurrNet/commit/cb55fe2c50d71f7bc01e1d3b4780b9f56df84a2b))

# [1.20.0-beta.249](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.248...v1.20.0-beta.249) (2026-07-03)


### Bug Fixes

* reapply ownership toggle state after spawn ([e412c65](https://github.com/PurrNet/PurrNet/commit/e412c65b68804dcce0b19558e52a0e43b02c3ef8))

# [1.20.0-beta.248](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.247...v1.20.0-beta.248) (2026-07-02)


### Bug Fixes

* NT and NR interpolation issues ([031b6f1](https://github.com/PurrNet/PurrNet/commit/031b6f19c86c555a1741d2c38424d7c37eadafc6))

# [1.20.0-beta.247](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.246...v1.20.0-beta.247) (2026-06-30)


### Bug Fixes

* update docs base URL ([406e30f](https://github.com/PurrNet/PurrNet/commit/406e30f822c574f9fb96e00a65220214316855d3))

# [1.20.0-beta.246](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.245...v1.20.0-beta.246) (2026-06-30)


### Bug Fixes

* defer NetworkOwnershipToggle refresh to LateUpdate ([cefdcfb](https://github.com/PurrNet/PurrNet/commit/cefdcfb01db807bd509508c01ce89f1fb808b2b8))
* discard deferred refreshes after despawn ([5459931](https://github.com/PurrNet/PurrNet/commit/5459931576898e51b5453a107f9787e360ca0c02))
* register delta serializers with NativeDeltaPacker in IL codegen ([e1ad40b](https://github.com/PurrNet/PurrNet/commit/e1ad40b2c751a17b3f36eeffc7ff1e4517dc4da3))

# [1.20.0-beta.245](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.244...v1.20.0-beta.245) (2026-06-30)


### Bug Fixes

* dont use BitPacker at the FragmentationLayer due to corrupting send array (it is freed on send and net "Get" risks getting the same array back and modifying it). ([187866b](https://github.com/PurrNet/PurrNet/commit/187866b8301c76a5443e62c5ce71c89b7f835989))

# [1.20.0-beta.244](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.243...v1.20.0-beta.244) (2026-06-30)


### Features

* introduce a hash resolver window to debug unregistered IDs and resolve them to their type ([09901d5](https://github.com/PurrNet/PurrNet/commit/09901d5b0ddc826674a3bbd37e0fe95e5983bff2))

# [1.20.0-beta.243](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.242...v1.20.0-beta.243) (2026-06-29)


### Bug Fixes

* Avoid ambiguity in steam naming ([b8cce5f](https://github.com/PurrNet/PurrNet/commit/b8cce5f4a6ee46f9f7d793239f59f916fd069fc7))

# [1.20.0-beta.242](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.241...v1.20.0-beta.242) (2026-06-29)


### Bug Fixes

* allow to make NetworkRigidbody error relative to the parent ([d28d03f](https://github.com/PurrNet/PurrNet/commit/d28d03fa0c06514f9c7cbce0cf9729cfed0189ad))

# [1.20.0-beta.241](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.240...v1.20.0-beta.241) (2026-06-29)


### Bug Fixes

* allow NetworkRigidbody to use the parent for velocity calculations ([7bafd6b](https://github.com/PurrNet/PurrNet/commit/7bafd6b73c0287ea13409cd79ad00a417fc8c421))
* reworking some early LOD stuff ([200db77](https://github.com/PurrNet/PurrNet/commit/200db77361976fb6febeeb47221558e5c0718bd4))

# [1.20.0-beta.240](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.239...v1.20.0-beta.240) (2026-06-28)


### Bug Fixes

* allow manual spawn to access OnSerialize/Deserialize path ([e394da5](https://github.com/PurrNet/PurrNet/commit/e394da52786df72c78e3fbba3843bd712e9ea5fc))

# [1.20.0-beta.239](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.238...v1.20.0-beta.239) (2026-06-28)


### Bug Fixes

* deffer OnDeserialize before any OnEarlySpawn is called to avoid execution order mistakes ([09f35d7](https://github.com/PurrNet/PurrNet/commit/09f35d7b60562ab0180a35fcdbf201c3208688c1))

# [1.20.0-beta.238](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.237...v1.20.0-beta.238) (2026-06-28)


### Bug Fixes

* introduce some roslyn IDE warnings/errors for quick feedback ([d99e0da](https://github.com/PurrNet/PurrNet/commit/d99e0da50931a0fa3f1647c9555fd613f4870ae1))

# [1.20.0-beta.237](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.236...v1.20.0-beta.237) (2026-06-28)


### Bug Fixes

* GC pass across PurrNet ([de4a8c8](https://github.com/PurrNet/PurrNet/commit/de4a8c8b8b04e848a007de314b5a3a726aedc685))

# [1.20.0-beta.236](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.235...v1.20.0-beta.236) (2026-06-27)


### Bug Fixes

* remove proxy transport layer for now ([ebaac4b](https://github.com/PurrNet/PurrNet/commit/ebaac4bb07064801e2d2a04c548c778235b1f124))

# [1.20.0-beta.235](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.234...v1.20.0-beta.235) (2026-06-26)


### Bug Fixes

* Instantiate with InstantiateParameters ([20b8c9d](https://github.com/PurrNet/PurrNet/commit/20b8c9dc557d1d525d54d9e97bd3c4ea4f956281))

# [1.20.0-beta.234](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.233...v1.20.0-beta.234) (2026-06-26)


### Bug Fixes

* synchashset initial state ([c129afa](https://github.com/PurrNet/PurrNet/commit/c129afa6501425110db95de64026773309e5790e))

# [1.20.0-beta.233](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.232...v1.20.0-beta.233) (2026-06-26)


### Bug Fixes

* make sure GameObjectPrototype doesn't fail with disposed list ([3ab5832](https://github.com/PurrNet/PurrNet/commit/3ab5832c340aba402b2b9c4106704f5078919b4d))

# [1.20.0-beta.232](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.231...v1.20.0-beta.232) (2026-06-26)


### Bug Fixes

* introduce onTeleportCorrection event to NetworkRigidbody.cs ([7bb6457](https://github.com/PurrNet/PurrNet/commit/7bb645752c1d2753c7df830cdfc77f204559b2be))

# [1.20.0-beta.231](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.230...v1.20.0-beta.231) (2026-06-24)


### Bug Fixes

* better naming for internal transport ([55b2690](https://github.com/PurrNet/PurrNet/commit/55b2690f1aaee34fbc01889ffdf345204c812c36))

# [1.20.0-beta.230](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.229...v1.20.0-beta.230) (2026-06-24)


### Bug Fixes

* NetworkRigidbody.cs soft parent inconsistencies and added some inspector debugging ([d992bc3](https://github.com/PurrNet/PurrNet/commit/d992bc39dafa6eb98a450daaebd1b0651f997a02))
* undo rawTransport breaking change ([a066ff3](https://github.com/PurrNet/PurrNet/commit/a066ff3888deebd0f5d257187f20ec01580d8c44))

# [1.20.0-beta.229](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.228...v1.20.0-beta.229) (2026-06-23)


### Bug Fixes

* accept server cookie and PlayerID always ([b4980cc](https://github.com/PurrNet/PurrNet/commit/b4980cc078ae3e8fef46d93e825e9b25285afc1f))

# [1.20.0-beta.228](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.227...v1.20.0-beta.228) (2026-06-23)


### Bug Fixes

* remove redundant code in HierarchyV2.cs ([6bad369](https://github.com/PurrNet/PurrNet/commit/6bad36937154710ecd980992fb8bfb6a9a70cbdf))

# [1.20.0-beta.227](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.226...v1.20.0-beta.227) (2026-06-23)


### Bug Fixes

* patch some catchup logic, still not ideal need to revisit ([511d120](https://github.com/PurrNet/PurrNet/commit/511d12029aa62a069e2a2e34c0b387f9a5e5229f))

# [1.20.0-beta.226](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.225...v1.20.0-beta.226) (2026-06-23)


### Bug Fixes

* syncvar force sync; more pooling behavior ([c0db18e](https://github.com/PurrNet/PurrNet/commit/c0db18ec21b38522f1d3f0fd4059fd8e0f5b9649))

# [1.20.0-beta.225](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.224...v1.20.0-beta.225) (2026-06-22)


### Bug Fixes

* reconnect ([f934dd9](https://github.com/PurrNet/PurrNet/commit/f934dd97066bcbed4432e6c8ac164c6f713b6d87))

# [1.20.0-beta.224](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.223...v1.20.0-beta.224) (2026-06-22)


### Bug Fixes

* scene and syncvar bug ([9cd9732](https://github.com/PurrNet/PurrNet/commit/9cd973256e7478b37c1cb1f2a1ad82dfd6142a41))

# [1.20.0-beta.223](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.222...v1.20.0-beta.223) (2026-06-22)


### Bug Fixes

* include chidlren for scene objects ([5cbcbf2](https://github.com/PurrNet/PurrNet/commit/5cbcbf2f6e6ef10616988b86558a5615dd84bd3b))

# [1.20.0-beta.222](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.221...v1.20.0-beta.222) (2026-06-22)


### Bug Fixes

* pooling bugs ([d3c99ea](https://github.com/PurrNet/PurrNet/commit/d3c99ea9b204cb24602446e0fe2fab93eebaa3af))

# [1.20.0-beta.221](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.220...v1.20.0-beta.221) (2026-06-21)


### Bug Fixes

* pooling bugs ([22eef29](https://github.com/PurrNet/PurrNet/commit/22eef2930c13c802ef2be1baa0364ed301e6dfd0))

# [1.20.0-beta.220](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.219...v1.20.0-beta.220) (2026-06-21)


### Bug Fixes

* dont queue RPCs on migration ([5477dc2](https://github.com/PurrNet/PurrNet/commit/5477dc2a227974f617ce2135368fcb05c59a27cb))

# [1.20.0-beta.219](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.218...v1.20.0-beta.219) (2026-06-21)


### Bug Fixes

* player reconnects during host promotion ([00c7bfe](https://github.com/PurrNet/PurrNet/commit/00c7bfee493808c147f3ad6c0f110472114a55d9))

# [1.20.0-beta.218](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.217...v1.20.0-beta.218) (2026-06-21)


### Bug Fixes

* avoid disabling everything before the initial spawn ([0ebae28](https://github.com/PurrNet/PurrNet/commit/0ebae28bbff42eeb3d13b5b3ffca22f9639fb282))

# [1.20.0-beta.217](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.216...v1.20.0-beta.217) (2026-06-16)


### Bug Fixes

* ensure proper unsubscription in CompositeTransport ([32db46a](https://github.com/PurrNet/PurrNet/commit/32db46a1414daf629aa1fd0c10d899c31109330a))

# [1.20.0-beta.216](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.215...v1.20.0-beta.216) (2026-06-11)


### Bug Fixes

* handle player reconnection more gracefully in PlayersManager ([e0ad5b6](https://github.com/PurrNet/PurrNet/commit/e0ad5b686daa4631f245e9a450d122fa0805f935))

# [1.20.0-beta.215](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.214...v1.20.0-beta.215) (2026-06-11)


### Bug Fixes

* NetworkAnimator improvements and missing methods ([3b6774a](https://github.com/PurrNet/PurrNet/commit/3b6774a82f7128530f8ec1c1e4d75a20a35a4bde))

# [1.20.0-beta.214](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.213...v1.20.0-beta.214) (2026-06-10)


### Bug Fixes

* Add network condition change callbacks ([1436536](https://github.com/PurrNet/PurrNet/commit/14365369d12433ad67881f2ac5f15d3b1b0fbd1f))

# [1.20.0-beta.213](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.212...v1.20.0-beta.213) (2026-06-09)


### Features

* IPersistentPrefabProvider and PersistentId ([85b2d18](https://github.com/PurrNet/PurrNet/commit/85b2d18ee5c661bbe7cfee589a013fc16502248e))

# [1.20.0-beta.212](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.211...v1.20.0-beta.212) (2026-06-06)


### Bug Fixes

* syncvar issue + tests ([cba8532](https://github.com/PurrNet/PurrNet/commit/cba85326268297df4bae80e3342b0f27f97fa9b0))

# [1.20.0-beta.211](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.210...v1.20.0-beta.211) (2026-06-06)


### Bug Fixes

* dedicated server syncvar tests were failing on reconnection ([24ef538](https://github.com/PurrNet/PurrNet/commit/24ef538220b194a9d4bcfc959f7d53fac93f4fd6))

# [1.20.0-beta.210](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.209...v1.20.0-beta.210) (2026-06-06)


### Bug Fixes

* SyncVar reconnect sync issues ([c6d9cbb](https://github.com/PurrNet/PurrNet/commit/c6d9cbbdbc22e9ee392b1b863584a1dc52440d46))

# [1.20.0-beta.209](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.208...v1.20.0-beta.209) (2026-06-05)


### Bug Fixes

* Network RB handoff on ownership change ([184d1b9](https://github.com/PurrNet/PurrNet/commit/184d1b9bd23eb83922cd0bcac9cec7a7e9e37d72))

# [1.20.0-beta.208](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.207...v1.20.0-beta.208) (2026-06-04)


### Bug Fixes

* PurrNet packages window improvements ([ea1db92](https://github.com/PurrNet/PurrNet/commit/ea1db92c35699c60f1b4aaee82b3f5b7725bc28b))
* Rebuild ([042fd1a](https://github.com/PurrNet/PurrNet/commit/042fd1a87d507fef7da532077045f236dd42b919))

# [1.20.0-beta.207](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.206...v1.20.0-beta.207) (2026-05-31)


### Bug Fixes

* State machine race condition ([6cdd8e7](https://github.com/PurrNet/PurrNet/commit/6cdd8e7ba7117b7519088e42f6b6baad89b202ca))

# [1.20.0-beta.206](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.205...v1.20.0-beta.206) (2026-05-31)


### Bug Fixes

* Add server transport startup control to composite transport ([e0c4a0c](https://github.com/PurrNet/PurrNet/commit/e0c4a0ca68620e6a831da702a07619f399a39671))

# [1.20.0-beta.205](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.204...v1.20.0-beta.205) (2026-05-31)


### Bug Fixes

* Ownership missing identity safety ([f6f9232](https://github.com/PurrNet/PurrNet/commit/f6f923235f517dc7675a6089ae02a42f26c45ce4))
* State machine dedicated server state handling ([7659953](https://github.com/PurrNet/PurrNet/commit/7659953f6802dff9a906a8eab930dc20c527ee0d))

# [1.20.0-beta.204](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.203...v1.20.0-beta.204) (2026-05-31)


### Bug Fixes

* StateMachine insertion ([048c557](https://github.com/PurrNet/PurrNet/commit/048c557cb3e2cb527fa3c4901d7161459d495b07))

# [1.20.0-beta.203](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.202...v1.20.0-beta.203) (2026-05-29)


### Bug Fixes

* NRE resilience when despawning ([a8b788d](https://github.com/PurrNet/PurrNet/commit/a8b788d2abe608fc6f78d8e7d0de08ceae5d3c35))

# [1.20.0-beta.202](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.201...v1.20.0-beta.202) (2026-05-29)


### Bug Fixes

* skip unspawned network identities when building game object framework ([25f04a7](https://github.com/PurrNet/PurrNet/commit/25f04a7d484040961a53a4775b3f534402595896))

# [1.20.0-beta.201](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.200...v1.20.0-beta.201) (2026-05-29)


### Bug Fixes

* Visibility safeguard for null ([ce62c74](https://github.com/PurrNet/PurrNet/commit/ce62c7419da14050c37cf63825be2e7d6c39d266))

# [1.20.0-beta.200](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.199...v1.20.0-beta.200) (2026-05-29)


### Bug Fixes

* bump version ([69d7880](https://github.com/PurrNet/PurrNet/commit/69d7880dcf6f86872155a9f53137167b52956810))
* Nakama transport ([f68a4ea](https://github.com/PurrNet/PurrNet/commit/f68a4eaa9d6034d1aba15ed2d343adb7827a39a2))

# [1.20.0-beta.199](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.198...v1.20.0-beta.199) (2026-05-28)


### Bug Fixes

* Add null safety to hierarchy ([7722f33](https://github.com/PurrNet/PurrNet/commit/7722f3341e416f835ca5e10e531c1c3a6916a387))

# [1.20.0-beta.198](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.197...v1.20.0-beta.198) (2026-05-28)


### Bug Fixes

* Improve nested traversal of Asset scanners ([71e32df](https://github.com/PurrNet/PurrNet/commit/71e32dfeecbcba62651cbf3c761cc721aaa60a9a))

# [1.20.0-beta.197](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.196...v1.20.0-beta.197) (2026-05-28)


### Bug Fixes

* Validated syncvar old value inconsistency ([6473bf1](https://github.com/PurrNet/PurrNet/commit/6473bf1c2167edc6e5280b2c2c4bfdaba88ee698))

# [1.20.0-beta.196](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.195...v1.20.0-beta.196) (2026-05-28)


### Bug Fixes

* refactor `currentTransport` to match previous behavior ([0814dd8](https://github.com/PurrNet/PurrNet/commit/0814dd82b2a4554b65e2053846a4dda263a0dfe7))

# [1.20.0-beta.195](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.194...v1.20.0-beta.195) (2026-05-28)


### Bug Fixes

* proper cleanup when something is destroyed ([c216d35](https://github.com/PurrNet/PurrNet/commit/c216d35bbab123110063238741c448b96f83fd97))

# [1.20.0-beta.194](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.193...v1.20.0-beta.194) (2026-05-27)


### Bug Fixes

* dedicated server state missing for owner auth sync types on collections ([9991f3a](https://github.com/PurrNet/PurrNet/commit/9991f3ab1bb505810ad6a575d2a0bc6ca3ccd83c))

# [1.20.0-beta.193](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.192...v1.20.0-beta.193) (2026-05-27)


### Features

* async RPC fail fast on target disconnect + GC improvements ([d3d28f5](https://github.com/PurrNet/PurrNet/commit/d3d28f551febf2618f273d9c632277c61dfa0436))

# [1.20.0-beta.192](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.191...v1.20.0-beta.192) (2026-05-27)


### Bug Fixes

* SyncVar _isDirty hand-off bug ([1da4117](https://github.com/PurrNet/PurrNet/commit/1da411752d1a9fbb510786136b946ed364ae3d97))

# [1.20.0-beta.191](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.190...v1.20.0-beta.191) (2026-05-26)


### Features

* NetworkRigidbody.cs soft parenting ([c38ea9b](https://github.com/PurrNet/PurrNet/commit/c38ea9b12326142e686818a6b8d81b71f75cfc8c))

# [1.20.0-beta.190](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.189...v1.20.0-beta.190) (2026-05-25)


### Bug Fixes

* mathematic assembly not being loaded ([bbbbf7d](https://github.com/PurrNet/PurrNet/commit/bbbbf7d2fcc28bdbca5e7bd4335d4ed77488e44c))

# [1.20.0-beta.189](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.188...v1.20.0-beta.189) (2026-05-25)


### Bug Fixes

* cap extrapolation and handle state settling ([c7e04c3](https://github.com/PurrNet/PurrNet/commit/c7e04c35a766a3cd174fd8a027d284247e6f65d0))

# [1.20.0-beta.188](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.187...v1.20.0-beta.188) (2026-05-24)


### Bug Fixes

* Editor in 6.5 different callbacks ([761c34e](https://github.com/PurrNet/PurrNet/commit/761c34eb51c92b03e57c92068cc22ed7bbad7ad5))

# [1.20.0-beta.187](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.186...v1.20.0-beta.187) (2026-05-24)


### Bug Fixes

* Move to entity ID for newer versions that support it ([b833696](https://github.com/PurrNet/PurrNet/commit/b83369690f4daa24843ba3e5fbe4f35794b55a01))

# [1.20.0-beta.186](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.185...v1.20.0-beta.186) (2026-05-21)


### Features

* cache `hasConnectedOwner` (expenssive getter) ([b54bc66](https://github.com/PurrNet/PurrNet/commit/b54bc66d3229ca09d09ee19d8cb943ecc6f7ff9c))

# [1.20.0-beta.185](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.184...v1.20.0-beta.185) (2026-05-21)


### Bug Fixes

* resolve absolute reference before OnObserverAdded events ([0ddeb8d](https://github.com/PurrNet/PurrNet/commit/0ddeb8da48ed24d9725064bde7d48f651aaaa9f7))

# [1.20.0-beta.184](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.183...v1.20.0-beta.184) (2026-05-21)


### Bug Fixes

* senders serialized customData breadth-first while the receiver deserialized it depth-first. ([29e5daa](https://github.com/PurrNet/PurrNet/commit/29e5daa93a69b94deeef8d2ea9ae50edc7933237))

# [1.20.0-beta.183](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.182...v1.20.0-beta.183) (2026-05-21)


### Bug Fixes

* GetFullPrototype wasn't including all children just the first one for all gameobjects ([926ee40](https://github.com/PurrNet/PurrNet/commit/926ee401655df0bbfbc79edd6f642ed7da95798e))

# [1.20.0-beta.182](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.181...v1.20.0-beta.182) (2026-05-21)


### Bug Fixes

* use manager local player (server would check for default...) ([a306011](https://github.com/PurrNet/PurrNet/commit/a30601138cd8835a3381a4f704e6925ea8e6c235))

# [1.20.0-beta.181](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.180...v1.20.0-beta.181) (2026-05-21)


### Bug Fixes

* IL processing self references security ([4ed7e5d](https://github.com/PurrNet/PurrNet/commit/4ed7e5dee308ef96502edf28a053eae323215aea))

# [1.20.0-beta.180](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.179...v1.20.0-beta.180) (2026-05-21)


### Features

* OnSerialize/OnDeserialize for spawn coupled data ([cf93d09](https://github.com/PurrNet/PurrNet/commit/cf93d09c0c6eb47c788e97e944878d3bd9792b3f))

# [1.20.0-beta.179](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.178...v1.20.0-beta.179) (2026-05-20)


### Bug Fixes

* NetworkRigidbody allow to tp locally ([81c6bf4](https://github.com/PurrNet/PurrNet/commit/81c6bf4ecfb85e02bb7f2ed339c478214c1f4b7f))

# [1.20.0-beta.178](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.177...v1.20.0-beta.178) (2026-05-20)


### Bug Fixes

* NetworkRigidbody handoff ([63ee82b](https://github.com/PurrNet/PurrNet/commit/63ee82be3d21fd273bc0005bb974002af938136b))

# [1.20.0-beta.177](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.176...v1.20.0-beta.177) (2026-05-20)


### Bug Fixes

* NetworkRigidbody teleport rigidbody left state and the interpolation buffer stale ([20e1331](https://github.com/PurrNet/PurrNet/commit/20e133151f7bb98418a69e9d6975a95a419ce4f7))

# [1.20.0-beta.176](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.175...v1.20.0-beta.176) (2026-05-18)


### Bug Fixes

* timing issues with NT ([ed46035](https://github.com/PurrNet/PurrNet/commit/ed4603510798b8bafc4754ee7b84784c964432eb))

# [1.20.0-beta.175](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.174...v1.20.0-beta.175) (2026-05-18)


### Bug Fixes

* don't override NT's pos when no valid data is present (yet) ([88360a0](https://github.com/PurrNet/PurrNet/commit/88360a07436e0b19985f635d3a3ac77ddf7375ee))

# [1.20.0-beta.174](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.173...v1.20.0-beta.174) (2026-05-18)


### Bug Fixes

* teleport should... teleport ([c7e40e8](https://github.com/PurrNet/PurrNet/commit/c7e40e8d2031ba0b66f5441c5c782526b7079d6c))

# [1.20.0-beta.173](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.172...v1.20.0-beta.173) (2026-05-18)


### Bug Fixes

* position driver with local coords ([d0e8309](https://github.com/PurrNet/PurrNet/commit/d0e830911aa5ec6cc70c871d5e921bcdaa6f17f8))

# [1.20.0-beta.172](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.171...v1.20.0-beta.172) (2026-05-18)


### Bug Fixes

* snapshot jumping with world shifting on NR ([c406040](https://github.com/PurrNet/PurrNet/commit/c406040a6fab9c14e31222167864ff27d4d81daa))

# [1.20.0-beta.171](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.170...v1.20.0-beta.171) (2026-05-18)


### Features

* allow position transform similar to networkrigidbody transform but for NetworkTransform.cs ([4a06b8e](https://github.com/PurrNet/PurrNet/commit/4a06b8e100b62a5b0cefa290862c8095abd9237a))

# [1.20.0-beta.170](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.169...v1.20.0-beta.170) (2026-05-18)


### Bug Fixes

* don't print public user IPs in the inspector (NAT debug view) ([413231e](https://github.com/PurrNet/PurrNet/commit/413231e257a70521bcb55aa42d48e95dc2e4fa07))

# [1.20.0-beta.169](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.168...v1.20.0-beta.169) (2026-05-18)


### Bug Fixes

* move mathematics packer to it's own assembly to avoid having others to reference it ([71a23a0](https://github.com/PurrNet/PurrNet/commit/71a23a014111150f64455339dfe82e71a7ba62fd))

# [1.20.0-beta.168](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.167...v1.20.0-beta.168) (2026-05-18)


### Bug Fixes

* PurrTransport NAT bugs and editor rendering ([cb25abb](https://github.com/PurrNet/PurrNet/commit/cb25abbc44952b27a98c7a82f23dff1108629018))


### Features

* NAT p2p support for PurrTransport ([b8ee129](https://github.com/PurrNet/PurrNet/commit/b8ee12926c1db6de013d643027082b3118764e3c))

# [1.20.0-beta.167](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.166...v1.20.0-beta.167) (2026-05-17)


### Bug Fixes

* Cleanup Network RB kinematic handling ([6a16ffe](https://github.com/PurrNet/PurrNet/commit/6a16ffeaf9024dbd01f1e845c486c944ffacda03))

# [1.20.0-beta.166](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.165...v1.20.0-beta.166) (2026-05-16)


### Bug Fixes

* make RigidbodyStateData encode it's position frame to avoid race conditions on the remotes ([7394471](https://github.com/PurrNet/PurrNet/commit/739447198bf15c49001d9c34088696f761c3ab8d))

# [1.20.0-beta.165](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.164...v1.20.0-beta.165) (2026-05-16)


### Features

* NetworkRigidbody.cs position driver ([da26c4f](https://github.com/PurrNet/PurrNet/commit/da26c4fea1e3484009d0886f9604c8f73714a894))

# [1.20.0-beta.164](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.163...v1.20.0-beta.164) (2026-05-14)


### Bug Fixes

* store a client owner version for correct OnOwnerChanged callbacks in host mode ([a12293b](https://github.com/PurrNet/PurrNet/commit/a12293bbccf925c6f30632ad909ba208e37055ca))


### Features

* introduce a thin layer between purrnet and the transport for some finer control ([b26c76c](https://github.com/PurrNet/PurrNet/commit/b26c76c39a0c4aab477dba173fb9e5e9e7b3568a))

# [1.20.0-beta.163](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.162...v1.20.0-beta.163) (2026-05-14)


### Bug Fixes

* allow SyncBigData base contructor to shine through :) ([a16891c](https://github.com/PurrNet/PurrNet/commit/a16891cbb0c8304f5571a8f08cc6409481db5503))

# [1.20.0-beta.162](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.161...v1.20.0-beta.162) (2026-05-14)


### Bug Fixes

* make auth denial reason nullable ([10c701f](https://github.com/PurrNet/PurrNet/commit/10c701f44350ff2d4d15bd21cff8055bf993c8d6))

# [1.20.0-beta.161](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.160...v1.20.0-beta.161) (2026-05-14)


### Features

* add variant of AuthenticationBehaviour that can give a deny response to client ([b0edd85](https://github.com/PurrNet/PurrNet/commit/b0edd85c4329f0eb9b71dc67499fefe5fabe14d3))

# [1.20.0-beta.160](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.159...v1.20.0-beta.160) (2026-05-14)


### Bug Fixes

* don't delay spawn packet by one tick ([bdbcd52](https://github.com/PurrNet/PurrNet/commit/bdbcd5215a2a7094c95489620d3e269bf7c06196))

# [1.20.0-beta.159](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.158...v1.20.0-beta.159) (2026-05-13)


### Bug Fixes

* Improved performance of UnifiedAssetPostprocessor ([6454bec](https://github.com/PurrNet/PurrNet/commit/6454becf5b7d5dd489db38e2982cdbe047ec972f))

# [1.20.0-beta.158](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.157...v1.20.0-beta.158) (2026-05-12)


### Bug Fixes

* update purrnet packages with bug fixes and performance improvements ([43cce1a](https://github.com/PurrNet/PurrNet/commit/43cce1a374d7bd39c448272cb997d8508f8107ac))

# [1.20.0-beta.157](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.156...v1.20.0-beta.157) (2026-05-12)


### Bug Fixes

* parent change flush rpcs before sending packet ([432def8](https://github.com/PurrNet/PurrNet/commit/432def8b0657118a99f157c99217b76516d82613))

# [1.20.0-beta.156](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.155...v1.20.0-beta.156) (2026-05-12)


### Bug Fixes

* NetworkRigidbody only sync details if there is actually a rigidbody component ([f9ebafe](https://github.com/PurrNet/PurrNet/commit/f9ebafeb9d38fd7740fe91052cc12fdeac0d8bd5))

# [1.20.0-beta.155](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.154...v1.20.0-beta.155) (2026-05-11)


### Bug Fixes

* observer addition for spawner ([ede2fe3](https://github.com/PurrNet/PurrNet/commit/ede2fe365d9bde7d197d34d918fae46c0f591b89))

# [1.20.0-beta.154](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.153...v1.20.0-beta.154) (2026-05-11)


### Bug Fixes

* fix some timing issues with OnObserverAdded and packets being split across multiple ticks ([fe50eb8](https://github.com/PurrNet/PurrNet/commit/fe50eb8296ec7021d7fbffa1fd75f20fb32e639f))

# [1.20.0-beta.153](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.152...v1.20.0-beta.153) (2026-05-11)


### Bug Fixes

* delay player catchup to once they are marked as having the scene loaded ([01445c0](https://github.com/PurrNet/PurrNet/commit/01445c082b28135d492848a18fb0a1f8dcc40be7))

# [1.20.0-beta.152](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.151...v1.20.0-beta.152) (2026-05-11)


### Bug Fixes

* loopback for async RPC exceptions ([3365282](https://github.com/PurrNet/PurrNet/commit/3365282488743c2ec706e8def8734b1bcadf0424))

# [1.20.0-beta.151](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.150...v1.20.0-beta.151) (2026-05-11)


### Bug Fixes

* ownership callbacks order ([2b1dff1](https://github.com/PurrNet/PurrNet/commit/2b1dff128e44fa241717385243a97375497560fb))

# [1.20.0-beta.150](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.149...v1.20.0-beta.150) (2026-05-10)


### Bug Fixes

* Cleanup of Network RB calls to self ([6f20e5a](https://github.com/PurrNet/PurrNet/commit/6f20e5ac945b132536826377afc41d752428eb60))
* Performance optimizations for Network Rigidbody ([f2be755](https://github.com/PurrNet/PurrNet/commit/f2be75513816b8779bec762346b713e69214d692))

# [1.20.0-beta.149](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.148...v1.20.0-beta.149) (2026-05-10)


### Bug Fixes

* host-loopback issue for async RPCs ([56e8ad3](https://github.com/PurrNet/PurrNet/commit/56e8ad336894bf0127e9a5900f52681604ce461b))

# [1.20.0-beta.148](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.147...v1.20.0-beta.148) (2026-05-10)


### Bug Fixes

* if _raw_rules.Count is 0 it never adds the rule ([c884cfb](https://github.com/PurrNet/PurrNet/commit/c884cfb21f7bf5ef65b5a41f7f889271d9898809))

# [1.20.0-beta.147](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.146...v1.20.0-beta.147) (2026-05-10)


### Bug Fixes

* trigger server callbacks on the correct module (async packables need it) ([0ab9f5e](https://github.com/PurrNet/PurrNet/commit/0ab9f5e4d771a4ebc943efe445f6d6105cc2819d))

# [1.20.0-beta.146](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.145...v1.20.0-beta.146) (2026-05-10)


### Bug Fixes

* undo previous change regarding TargetRPC routing when HOST ([4a4fe88](https://github.com/PurrNet/PurrNet/commit/4a4fe88c69436ace522a8481bab0d7accd0b55a0))

# [1.20.0-beta.145](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.144...v1.20.0-beta.145) (2026-05-10)


### Bug Fixes

* some more disposable collections copying edgecases ([c6efbe8](https://github.com/PurrNet/PurrNet/commit/c6efbe8d146b5ab2ae66bab8761b30a5db0bf491))

# [1.20.0-beta.144](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.143...v1.20.0-beta.144) (2026-05-10)


### Bug Fixes

* properly copy when delta packing lists ([9f2c2a5](https://github.com/PurrNet/PurrNet/commit/9f2c2a5329aff8ae25f18e59e3f813fbb07c6f8e))

# [1.20.0-beta.143](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.142...v1.20.0-beta.143) (2026-05-10)


### Bug Fixes

* Network RB safety for runtime destroyed RB ([9d151d2](https://github.com/PurrNet/PurrNet/commit/9d151d295f9fd7940296e55a28aedbbe1e63febd))

# [1.20.0-beta.142](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.141...v1.20.0-beta.142) (2026-05-10)


### Bug Fixes

* Stop requiring RB on the NetworkRB ([1758f88](https://github.com/PurrNet/PurrNet/commit/1758f882097c41f3e419f5fabbbec5276dd623c8))

# [1.20.0-beta.141](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.140...v1.20.0-beta.141) (2026-05-10)


### Bug Fixes

* Default network RB to owner auth ([a4c1095](https://github.com/PurrNet/PurrNet/commit/a4c10957ee01db85c53fd6075d3523c93ba14bae))

# [1.20.0-beta.140](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.139...v1.20.0-beta.140) (2026-05-07)


### Bug Fixes

* composite transport disconnect reason ([63d6c79](https://github.com/PurrNet/PurrNet/commit/63d6c7962d173f3b6559aa9643bc4f7609a46d97))

# [1.20.0-beta.139](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.138...v1.20.0-beta.139) (2026-05-07)


### Bug Fixes

* targetrpc host scenarios, asyncpackable host scenarios ([5bfb9c8](https://github.com/PurrNet/PurrNet/commit/5bfb9c8285d2c22320e11e67b478a1875d7045ea))

# [1.20.0-beta.138](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.137...v1.20.0-beta.138) (2026-05-07)


### Bug Fixes

* TargetRPC bufferLast was not recording per target but globally ([bd58bb6](https://github.com/PurrNet/PurrNet/commit/bd58bb623d3d1fb74c808b3ba5887757e205aabb))

# [1.20.0-beta.137](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.136...v1.20.0-beta.137) (2026-05-07)


### Bug Fixes

* bring back the cached `LateLateUpdate` delegate for NetworkTransform.cs ([0850c26](https://github.com/PurrNet/PurrNet/commit/0850c2692a83d74cf16e4be156a5fae9318b6499))

# [1.20.0-beta.136](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.135...v1.20.0-beta.136) (2026-05-07)


### Bug Fixes

* deltaPacked with bufferLast causes issues so throw a compiler error for now atleast ([482305b](https://github.com/PurrNet/PurrNet/commit/482305b8379c6fe093e82c797a843a5a61df6e77))

# [1.20.0-beta.135](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.134...v1.20.0-beta.135) (2026-05-07)


### Bug Fixes

* NetworkTransform.cs prefer the smoother OnEnable version ([c744009](https://github.com/PurrNet/PurrNet/commit/c744009e40de38825ec3a3a891952d4b24709d47))

# [1.20.0-beta.134](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.133...v1.20.0-beta.134) (2026-05-07)


### Bug Fixes

* what an idiot... i need to sleep man; networktransform fixes ([9335560](https://github.com/PurrNet/PurrNet/commit/9335560002b1bd68c8b7011320a59d4e503b7766))

# [1.20.0-beta.133](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.132...v1.20.0-beta.133) (2026-05-06)


### Bug Fixes

* don't force sync to host's own player ([8d95b01](https://github.com/PurrNet/PurrNet/commit/8d95b0100379fdb3c2c85db675e33a4e6845768c))

# [1.20.0-beta.132](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.131...v1.20.0-beta.132) (2026-05-06)


### Bug Fixes

* client should not update it's last read state from current data, that just seems wrong ([641d33c](https://github.com/PurrNet/PurrNet/commit/641d33ceb49fbb412d0ae69dc5e4b8749a23c041))

# [1.20.0-beta.131](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.130...v1.20.0-beta.131) (2026-05-06)


### Bug Fixes

* stale NetworkTransform.cs data when re-enabling component ([5332464](https://github.com/PurrNet/PurrNet/commit/53324645257f1c854c0a1915fb398f625317379d))

# [1.20.0-beta.130](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.129...v1.20.0-beta.130) (2026-05-06)


### Bug Fixes

* respet `DontSyncHashes` for non auto events ([59b55d5](https://github.com/PurrNet/PurrNet/commit/59b55d5a01b755fd6aa9adafbf5c84d20f8dd2bd))

# [1.20.0-beta.129](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.128...v1.20.0-beta.129) (2026-05-06)


### Bug Fixes

* async packable inconsistency + tests for it ([b37eee1](https://github.com/PurrNet/PurrNet/commit/b37eee130146989a05120a56930bf00435d246c9))

# [1.20.0-beta.128](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.127...v1.20.0-beta.128) (2026-05-06)


### Bug Fixes

* Statistics manager GUI for builds ([8e28039](https://github.com/PurrNet/PurrNet/commit/8e28039595d0780670e25d666f342389da0044db))

# [1.20.0-beta.127](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.126...v1.20.0-beta.127) (2026-05-05)


### Bug Fixes

* New potential approach to network assets ([be46b14](https://github.com/PurrNet/PurrNet/commit/be46b145806fc5045f485b6520d7f7e153c3d2c6))

# [1.20.0-beta.126](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.125...v1.20.0-beta.126) (2026-05-05)


### Bug Fixes

* Add reference comparer to Network Assets for IL2CPP ([a44f49d](https://github.com/PurrNet/PurrNet/commit/a44f49d08df727d0751db0c540ef273f646c519d))

# [1.20.0-beta.125](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.124...v1.20.0-beta.125) (2026-05-05)


### Bug Fixes

* allow to supress auto ownership ([bcafea5](https://github.com/PurrNet/PurrNet/commit/bcafea5cea782cc4b147a93776e86b1d9bc34aa1))

# [1.20.0-beta.124](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.123...v1.20.0-beta.124) (2026-05-04)


### Bug Fixes

* add fallback to rebuild asset lookup if bake is missing ([a59cf71](https://github.com/PurrNet/PurrNet/commit/a59cf717f29e24930701ffeb75efa9c3e8635229))

# [1.20.0-beta.123](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.122...v1.20.0-beta.123) (2026-05-03)


### Bug Fixes

* Generic RPCs fixes and improvemets + tests ([c40665c](https://github.com/PurrNet/PurrNet/commit/c40665c07f3ad58902d735473bf8d7a09e8b897f))

# [1.20.0-beta.122](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.121...v1.20.0-beta.122) (2026-05-03)


### Bug Fixes

* async RPCs, add a fail fast path ([365bcda](https://github.com/PurrNet/PurrNet/commit/365bcda32738909f1d892c43c28061848e76401d))

# [1.20.0-beta.121](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.120...v1.20.0-beta.121) (2026-05-03)


### Bug Fixes

* inconsistent rules check with async RPCs and RequireServer rule ([2dc72af](https://github.com/PurrNet/PurrNet/commit/2dc72af26adfa491bfc7d40bb4ddc80b4b42f122))

# [1.20.0-beta.120](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.119...v1.20.0-beta.120) (2026-05-03)


### Bug Fixes

* exit code for tests ([44915ff](https://github.com/PurrNet/PurrNet/commit/44915ffa08eebabc96efae57730f70df953edce3))

# [1.20.0-beta.119](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.118...v1.20.0-beta.119) (2026-05-03)


### Bug Fixes

* lambda RPC not being resolved properly ([362c044](https://github.com/PurrNet/PurrNet/commit/362c044061ac2c2010102bec8d0aedb279bfc9f5))

# [1.20.0-beta.118](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.117...v1.20.0-beta.118) (2026-05-02)


### Bug Fixes

* dont save PurrNet version in the ApplicationConstants.json to avoid git changes for not much value ([f59651f](https://github.com/PurrNet/PurrNet/commit/f59651f37eab736cf4b0fd5c97168270a984c795))

# [1.20.0-beta.117](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.116...v1.20.0-beta.117) (2026-05-01)


### Bug Fixes

* useDelta for single TargetRpc ([ce695f7](https://github.com/PurrNet/PurrNet/commit/ce695f729934653be38806fd2f340d4dfd8687f4))

# [1.20.0-beta.116](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.115...v1.20.0-beta.116) (2026-05-01)


### Bug Fixes

* Network RB initial settings strip delta packing ([4d08208](https://github.com/PurrNet/PurrNet/commit/4d082084989d7370894f4cabfa5e94c63bdb7c9b))

# [1.20.0-beta.115](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.114...v1.20.0-beta.115) (2026-05-01)


### Bug Fixes

* Missing using directive ([f2a6720](https://github.com/PurrNet/PurrNet/commit/f2a6720542b089c7b295afdda7a67d123c782053))

# [1.20.0-beta.114](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.113...v1.20.0-beta.114) (2026-04-30)


### Bug Fixes

* update NetworkTransform.cs _cachedIsController when OnOwnerDisconnected ([b67aa48](https://github.com/PurrNet/PurrNet/commit/b67aa48ebdd82a2dd79df09d6ee584841f93977e))

# [1.20.0-beta.113](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.112...v1.20.0-beta.113) (2026-04-30)


### Features

* better diagnosis logs for missmatching types and for RPC exceptions ([92470e3](https://github.com/PurrNet/PurrNet/commit/92470e33ac1fd1274ff4b3958456318473fecb0c))

# [1.20.0-beta.112](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.111...v1.20.0-beta.112) (2026-04-29)


### Bug Fixes

* Validated syncvar can now be server authored as well ([1f3de10](https://github.com/PurrNet/PurrNet/commit/1f3de10be3f9c89e212640007ce4b713b35ebc81))

# [1.20.0-beta.111](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.110...v1.20.0-beta.111) (2026-04-29)


### Bug Fixes

* include UniTask in the type discovery ([439f992](https://github.com/PurrNet/PurrNet/commit/439f9929b4f934042bd1159d2167ffd155855a8e))

# [1.20.0-beta.110](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.109...v1.20.0-beta.110) (2026-04-28)


### Bug Fixes

* first-time buffering path doesn't respect the BitData's bitOrigin ([bd99a9d](https://github.com/PurrNet/PurrNet/commit/bd99a9daab9200c2316acfdfef7f0175d6f74c68))

# [1.20.0-beta.109](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.108...v1.20.0-beta.109) (2026-04-28)


### Bug Fixes

* inheritance and NetworkModules ([e3c145a](https://github.com/PurrNet/PurrNet/commit/e3c145a78cfacfe82d49b71b70579240b1aaed95))

# [1.20.0-beta.108](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.107...v1.20.0-beta.108) (2026-04-27)


### Bug Fixes

* host logic for Nakama ([02e66d7](https://github.com/PurrNet/PurrNet/commit/02e66d78aa01fb6395227789edaa018f5206b1e0))

# [1.20.0-beta.107](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.106...v1.20.0-beta.107) (2026-04-27)


### Bug Fixes

* Network RB late join stuff ([68b2172](https://github.com/PurrNet/PurrNet/commit/68b217205df1d6673d42a42e62d8bf0d671ef283))

# [1.20.0-beta.106](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.105...v1.20.0-beta.106) (2026-04-27)


### Bug Fixes

* Add force sync window to network rigidbody ([c7ddedb](https://github.com/PurrNet/PurrNet/commit/c7ddedbb183a7f7b48ac5088373b09ccf9f9b1ae))

# [1.20.0-beta.105](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.104...v1.20.0-beta.105) (2026-04-27)


### Bug Fixes

* supress some warnings for NakamaTransport.cs when it isnt installed ([29c554a](https://github.com/PurrNet/PurrNet/commit/29c554af847a2cc53b34f504723ca2fd264e9029))

# [1.20.0-beta.104](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.103...v1.20.0-beta.104) (2026-04-27)


### Bug Fixes

* allow NakamaTransport.cs to link to existing match id when starting server ([69a75b2](https://github.com/PurrNet/PurrNet/commit/69a75b2cb0d31f283c9ad2919c31f021ff115f97))

# [1.20.0-beta.103](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.102...v1.20.0-beta.103) (2026-04-27)


### Bug Fixes

* external git commit hash issue with the package manager ([ad51f09](https://github.com/PurrNet/PurrNet/commit/ad51f095b9f1a6c0315d19d8b6f500c62c41183c))


### Features

* add nakama transport ([730ef5e](https://github.com/PurrNet/PurrNet/commit/730ef5e918747429791c8939c215c9261c2bce01))

# [1.20.0-beta.102](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.101...v1.20.0-beta.102) (2026-04-26)


### Bug Fixes

* always ensure application constants loaded ([291863a](https://github.com/PurrNet/PurrNet/commit/291863ab6d58ddaa2499a9abddc14babacb51b6e))

# [1.20.0-beta.101](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.100...v1.20.0-beta.101) (2026-04-26)


### Bug Fixes

* Network Rigidbody override settings factory instance ([3263f81](https://github.com/PurrNet/PurrNet/commit/3263f814c1107b16b34b0f22538565d6ee327baf))

# [1.20.0-beta.100](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.99...v1.20.0-beta.100) (2026-04-25)


### Features

* ApplicationConstants ([b62c62d](https://github.com/PurrNet/PurrNet/commit/b62c62d68782316bbf27e1c8fe577a3ce47758b7))

# [1.20.0-beta.99](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.98...v1.20.0-beta.99) (2026-04-25)


### Features

* add support for runtime animator controller and avatar in NetAnimator ([97524cf](https://github.com/PurrNet/PurrNet/commit/97524cf47ac1ce6eb95371522706273d3c49c3e9))

# [1.20.0-beta.98](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.97...v1.20.0-beta.98) (2026-04-25)


### Bug Fixes

* Move PurrTelemetry to internal ([830510c](https://github.com/PurrNet/PurrNet/commit/830510cc1272c3fa359b22a0a4f2c1c48d7a92a3))

# [1.20.0-beta.97](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.96...v1.20.0-beta.97) (2026-04-25)


### Bug Fixes

* add layer weight actions for non-zero layers in NetAnimatorActionBatch ([e662b6d](https://github.com/PurrNet/PurrNet/commit/e662b6d6a2ecd921f9b175dbfeb2ef0d8ec09ba2))

# [1.20.0-beta.96](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.95...v1.20.0-beta.96) (2026-04-22)


### Bug Fixes

* DisposableList ToString null exception ([ed7de11](https://github.com/PurrNet/PurrNet/commit/ed7de11fc7b760487ee987688ba1c7f43e9f81bf))

# [1.20.0-beta.95](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.94...v1.20.0-beta.95) (2026-04-22)


### Bug Fixes

* Add local syncing to network RB ([8c2faf1](https://github.com/PurrNet/PurrNet/commit/8c2faf12b8b39f4daa9d4e571be99c99cf672251))
* Network rigidbody local space scale issue ([2c9ed3b](https://github.com/PurrNet/PurrNet/commit/2c9ed3b59c45c852dca5248595e08f6874b06cbb))
* Parent syncing added to Network Rigidbody ([c4a0a89](https://github.com/PurrNet/PurrNet/commit/c4a0a890b4ce862248941f7d902c3d5ba835df5b))

# [1.20.0-beta.94](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.93...v1.20.0-beta.94) (2026-04-22)


### Bug Fixes

* cache the TickModule when subscribing inside the SyncVar and use it for unsubscribing for consistency ([7e14941](https://github.com/PurrNet/PurrNet/commit/7e14941bbd4019f0090e500373a456e97fdfdf0c))

# [1.20.0-beta.93](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.92...v1.20.0-beta.93) (2026-04-21)


### Bug Fixes

* only install changed files with Purr Packages ([069037c](https://github.com/PurrNet/PurrNet/commit/069037c87cf1246e29fc9b78ee90c3b70a9b380a))

# [1.20.0-beta.92](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.91...v1.20.0-beta.92) (2026-04-21)


### Bug Fixes

* refactor package installation process to improve clarity and efficiency ([2c8b3bd](https://github.com/PurrNet/PurrNet/commit/2c8b3bd0e11747d5723d509880301d8e9e9a6809))

# [1.20.0-beta.91](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.90...v1.20.0-beta.91) (2026-04-21)


### Bug Fixes

* streamline SafeRemoveDirectory implementation for improved clarity and efficiency ([90f3200](https://github.com/PurrNet/PurrNet/commit/90f320085c55bac0da9cc76494bafd74492d69ee))

# [1.20.0-beta.90](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.89...v1.20.0-beta.90) (2026-04-20)


### Bug Fixes

* process ManualAddObserver events immediately for SyncVar parity ([9ba4759](https://github.com/PurrNet/PurrNet/commit/9ba475912f8eef60bd7fcd1d97d2a8778e4133fe))

# [1.20.0-beta.89](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.88...v1.20.0-beta.89) (2026-04-17)


### Bug Fixes

* improve ownership handling to prevent stale snapshots during observer updates ([2ddefa2](https://github.com/PurrNet/PurrNet/commit/2ddefa2e3dc818ab4b75f0bb94e7ce1d4211a6e7))

# [1.20.0-beta.88](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.87...v1.20.0-beta.88) (2026-04-17)


### Bug Fixes

* observers not updating `_latestData` properly ([65dd9d4](https://github.com/PurrNet/PurrNet/commit/65dd9d4937ecc73c34e87279a114565cea3cfe09))

# [1.20.0-beta.87](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.86...v1.20.0-beta.87) (2026-04-16)


### Bug Fixes

* Make asset processing more efficient ([7a9112a](https://github.com/PurrNet/PurrNet/commit/7a9112af285cf45333b6ed32ac5c0fa56860ad54))

# [1.20.0-beta.86](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.85...v1.20.0-beta.86) (2026-04-16)


### Bug Fixes

* Network Rigidbody snapshot fix for server only ([7afa796](https://github.com/PurrNet/PurrNet/commit/7afa7962309a4b3e1174c53e01906c45653411be))

# [1.20.0-beta.85](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.84...v1.20.0-beta.85) (2026-04-16)


### Bug Fixes

* add reset of velocity on hard correction ([8f52be6](https://github.com/PurrNet/PurrNet/commit/8f52be6d3772fd21de97ec5eebe2bce520bae0d7))

# [1.20.0-beta.84](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.83...v1.20.0-beta.84) (2026-04-16)


### Bug Fixes

* prefer syncvar naming for SyncLazyRef ([5fe9837](https://github.com/PurrNet/PurrNet/commit/5fe983797efe2505b082fe02fb6be6b62d49e25b))

# [1.20.0-beta.83](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.82...v1.20.0-beta.83) (2026-04-16)


### Features

* add GlobalNetworkID and SyncLazyRef classes for lazy network identity synchronization ([aed8940](https://github.com/PurrNet/PurrNet/commit/aed8940cf47c936bcf3e774dcda90904c498dd47))

# [1.20.0-beta.82](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.81...v1.20.0-beta.82) (2026-04-13)


### Features

* implement global control for auto start flags in NetworkManager ([745525e](https://github.com/PurrNet/PurrNet/commit/745525e600a9d7fa021e848fc95df2adaf6fc46f))

# [1.20.0-beta.81](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.80...v1.20.0-beta.81) (2026-04-13)


### Bug Fixes

* handle operation cancellation in transport connection logic (silence log) ([eb7ecce](https://github.com/PurrNet/PurrNet/commit/eb7eccec845326631ce409734a36d036026161b7))

# [1.20.0-beta.80](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.79...v1.20.0-beta.80) (2026-04-11)


### Bug Fixes

* Take project ID to purrversion json ([140cee2](https://github.com/PurrNet/PurrNet/commit/140cee244b76092cd0ac5efc36a76a0941ed6e78))

# [1.20.0-beta.79](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.78...v1.20.0-beta.79) (2026-04-11)


### Bug Fixes

* Improved telemetry for builds ([594d198](https://github.com/PurrNet/PurrNet/commit/594d198bdf0a4287e9849c00e4b66ae4757e8637))

# [1.20.0-beta.78](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.77...v1.20.0-beta.78) (2026-04-11)


### Bug Fixes

* app ID included with steam connection ([f315ce1](https://github.com/PurrNet/PurrNet/commit/f315ce15c081139967d9ee94733a3dc1075dc201))

# [1.20.0-beta.77](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.76...v1.20.0-beta.77) (2026-04-11)


### Bug Fixes

* update MTU retrieval logic for server and client transports ([8f837a4](https://github.com/PurrNet/PurrNet/commit/8f837a4ac6bf265135cceb5f0ce6e684c10693ee))

# [1.20.0-beta.76](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.75...v1.20.0-beta.76) (2026-04-11)


### Bug Fixes

* Implement anonymous telemetry ([9ede7cf](https://github.com/PurrNet/PurrNet/commit/9ede7cf3e0804a9cae556cbd2a1f5d643d16bba7))
* improve connection telemetry timing ([29ffe65](https://github.com/PurrNet/PurrNet/commit/29ffe6557ce00dd882fca902cb685bd08565bb6c))
* Telemetry project ID improved handling ([49e52bf](https://github.com/PurrNet/PurrNet/commit/49e52bf2e127a8eb5b038f5b4b0471b3856ce64e))

# [1.20.0-beta.75](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.74...v1.20.0-beta.75) (2026-04-11)


### Bug Fixes

* add exception handling for player event invocations to avoid breaking connection/disconnection flow ([a9b4e0e](https://github.com/PurrNet/PurrNet/commit/a9b4e0e2592f9ff8e087c4e2b7bc5779b409d7b5))

# [1.20.0-beta.74](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.73...v1.20.0-beta.74) (2026-04-11)


### Bug Fixes

* purr package manager cleanup patches ([64565ac](https://github.com/PurrNet/PurrNet/commit/64565ac70a474dc1fd64e6a44310bb54651f8920))

# [1.20.0-beta.73](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.72...v1.20.0-beta.73) (2026-04-11)


### Bug Fixes

* NetworkTransform performance improvements ([481d6ec](https://github.com/PurrNet/PurrNet/commit/481d6ecbd8a5c02c742cd24760758d958524245b))

# [1.20.0-beta.72](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.71...v1.20.0-beta.72) (2026-04-11)


### Bug Fixes

* Move away from GetInstanceID for asset handling ([763b9fb](https://github.com/PurrNet/PurrNet/commit/763b9fb5a42333e1783b6e1444c3ed4500a7ff6b))

# [1.20.0-beta.71](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.70...v1.20.0-beta.71) (2026-04-11)


### Bug Fixes

* impove Equality check for `NetworkTransformData` ([1f0a118](https://github.com/PurrNet/PurrNet/commit/1f0a118e9f26dcf8266b5696a8749a9eaf59e9a0))

# [1.20.0-beta.70](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.69...v1.20.0-beta.70) (2026-04-11)


### Bug Fixes

* redundant interpolation patches in GatherState from the NetworkTransform.cs ([e47ccad](https://github.com/PurrNet/PurrNet/commit/e47ccadad5d377b06c3012bf52bcaa39f4df7f27))

# [1.20.0-beta.69](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.68...v1.20.0-beta.69) (2026-04-11)


### Bug Fixes

* Allow compilation of PurrOnGUI outside editor defines ([c70deb2](https://github.com/PurrNet/PurrNet/commit/c70deb2b514d5b3257f3655548290ffded6488e0))

# [1.20.0-beta.68](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.67...v1.20.0-beta.68) (2026-04-11)


### Bug Fixes

* Move to singular dispatch OnGUI ([c787c49](https://github.com/PurrNet/PurrNet/commit/c787c49fb5f3588bc923f368a4dfd0035eda9ffa))

# [1.20.0-beta.67](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.66...v1.20.0-beta.67) (2026-04-11)


### Bug Fixes

* NetworkRigidbody OnGUI strip from builds ([ed4cff9](https://github.com/PurrNet/PurrNet/commit/ed4cff9de70e5dc5ec5e49491c5e66c87a23e832))

# [1.20.0-beta.66](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.65...v1.20.0-beta.66) (2026-04-11)


### Bug Fixes

* Utilize type caching avoiding async packable boxing ([5264cf7](https://github.com/PurrNet/PurrNet/commit/5264cf74cd6d27c593dc3334b9773c54611844da))

# [1.20.0-beta.65](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.64...v1.20.0-beta.65) (2026-04-11)


### Bug Fixes

* re-introduce the safety measures around scene switching, unity is volatile here ([46433f2](https://github.com/PurrNet/PurrNet/commit/46433f2663cd28dee4fad82ec7b1bd47988aaff2))

# [1.20.0-beta.64](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.63...v1.20.0-beta.64) (2026-04-11)


### Bug Fixes

* refactor how and when moving scenes happens when spawning objects ([fa1224b](https://github.com/PurrNet/PurrNet/commit/fa1224b2805a90259e6fab9d672e943a66a505b8))

# [1.20.0-beta.63](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.62...v1.20.0-beta.63) (2026-04-10)


### Bug Fixes

* RB issue ([e58dc40](https://github.com/PurrNet/PurrNet/commit/e58dc40cdf50367d37726e705447f212d362305f))

# [1.20.0-beta.62](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.61...v1.20.0-beta.62) (2026-04-08)


### Bug Fixes

* cache "has rigidbody" check for `NetworkTransform` ([b0c4081](https://github.com/PurrNet/PurrNet/commit/b0c4081711343520b95053da155af8db3d453473))

# [1.20.0-beta.61](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.60...v1.20.0-beta.61) (2026-04-08)


### Bug Fixes

* quick patch to SyncList/SyncDic/SyncArray to early exit from the OnTick event; ideally it should follow the SyncVar patern though ([19ce4b8](https://github.com/PurrNet/PurrNet/commit/19ce4b8d4c69a580be4ff0e146db544cff23a640))

# [1.20.0-beta.60](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.59...v1.20.0-beta.60) (2026-04-08)


### Bug Fixes

* remove TargetRpc fast path since it breaks ordering ([c701535](https://github.com/PurrNet/PurrNet/commit/c70153563a21ce3548a98d0332c496521ad5b5d4))

# [1.20.0-beta.59](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.58...v1.20.0-beta.59) (2026-04-08)


### Bug Fixes

* IL codegen for async RPCs ([8532f95](https://github.com/PurrNet/PurrNet/commit/8532f9570a55755ca33a4df76cc4abaf23cae1ee))

# [1.20.0-beta.58](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.57...v1.20.0-beta.58) (2026-04-08)


### Bug Fixes

* FieldAccessException for generic classes with NetworkModules under inheritance ([c648c35](https://github.com/PurrNet/PurrNet/commit/c648c350f168766b38e611392eee339e65077c2f))

# [1.20.0-beta.57](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.56...v1.20.0-beta.57) (2026-04-07)


### Bug Fixes

* added abstract settings override to NetworkRigidbody ([3735323](https://github.com/PurrNet/PurrNet/commit/3735323ece8a6f8507c0c1fc11ea59caa9b990bc))

# [1.20.0-beta.56](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.55...v1.20.0-beta.56) (2026-04-01)


### Bug Fixes

* rework DisposableHashSet, it's packers and add some tests to verify future changes ([334bc47](https://github.com/PurrNet/PurrNet/commit/334bc47a2ca7f8ab37afef3edd41bd70f9a86025))

# [1.20.0-beta.55](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.54...v1.20.0-beta.55) (2026-03-30)


### Bug Fixes

* missing meta file for LICENSE.txt ([0ca58c1](https://github.com/PurrNet/PurrNet/commit/0ca58c1dc6b83e0f4c512842a9b0f878d788c87f))

# [1.20.0-beta.54](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.53...v1.20.0-beta.54) (2026-03-30)


### Bug Fixes

* make sure NetworkRigidbody is fully spawned before we correct position and stuff ([415c061](https://github.com/PurrNet/PurrNet/commit/415c061b7e54b552a5459345d73f2bed18618e85))

# [1.20.0-beta.53](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.52...v1.20.0-beta.53) (2026-03-29)


### Bug Fixes

* Improved asset editors ([0a48f07](https://github.com/PurrNet/PurrNet/commit/0a48f075e74ed81a2c9d1300acc46e0ce1ad5a33))
* Searching added to asset management ([571c30d](https://github.com/PurrNet/PurrNet/commit/571c30dad31890a49c427dd79f9278f5c0915373))

# [1.20.0-beta.52](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.51...v1.20.0-beta.52) (2026-03-29)


### Bug Fixes

* Utilize collection index for asset management sorting ([6195823](https://github.com/PurrNet/PurrNet/commit/6195823cadea7a869784b2cd6f051440156ab266))

# [1.20.0-beta.51](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.50...v1.20.0-beta.51) (2026-03-28)


### Bug Fixes

* Fragmentation corruption fix ([0f12c24](https://github.com/PurrNet/PurrNet/commit/0f12c24f93cda1334c58fbd650dfbd9e7bb31b9e))

# [1.20.0-beta.50](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.49...v1.20.0-beta.50) (2026-03-28)


### Bug Fixes

* Push for version change ([8bdc25f](https://github.com/PurrNet/PurrNet/commit/8bdc25f5c71ee154f94403e3558aada5985207b9))

# [1.20.0-beta.49](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.48...v1.20.0-beta.49) (2026-03-27)


### Bug Fixes

* more ordering issues and better logging ([7b1b44b](https://github.com/PurrNet/PurrNet/commit/7b1b44b71b48c5568ff8fc7c16b3e9056fad684c))

# [1.20.0-beta.48](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.47...v1.20.0-beta.48) (2026-03-27)


### Bug Fixes

* SafeRemoveDirectory(folderPath) now runs before CleanupLegacyPackageFiles ([9a99877](https://github.com/PurrNet/PurrNet/commit/9a9987724b22cdc37da033fba190a9ac2a113df2))

# [1.20.0-beta.47](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.46...v1.20.0-beta.47) (2026-03-27)


### Bug Fixes

* delete should first move, then attempt to delete due to native libraries or opened files ([0206edc](https://github.com/PurrNet/PurrNet/commit/0206edc213886b5282b18dd2e94be28068bee46b))

# [1.20.0-beta.46](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.45...v1.20.0-beta.46) (2026-03-27)


### Bug Fixes

* use Packages folder instead such that clones respect it, also dont include version in the folder name ([9185c9c](https://github.com/PurrNet/PurrNet/commit/9185c9c423bc325c8d7129ea90f9656e56bac2b5))

# [1.20.0-beta.45](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.44...v1.20.0-beta.45) (2026-03-27)


### Bug Fixes

* LiteNetLib dont cache the available count ([40b44a3](https://github.com/PurrNet/PurrNet/commit/40b44a32be344f91adda783cd2bee6286bf372a1))

# [1.20.0-beta.44](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.43...v1.20.0-beta.44) (2026-03-27)


### Bug Fixes

* replace previous LiteNetLib code with just Unsafe.WriteUnaligned ([28164b7](https://github.com/PurrNet/PurrNet/commit/28164b7a9a00a98afc2a61e939d6f6cb5e0d9a2d))

# [1.20.0-beta.43](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.42...v1.20.0-beta.43) (2026-03-27)


### Bug Fixes

* LiteNetLib use safe path for all systems ([73b3493](https://github.com/PurrNet/PurrNet/commit/73b3493a5bad9ed3fab6985c766d5a01cac106d8))

# [1.20.0-beta.42](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.41...v1.20.0-beta.42) (2026-03-26)


### Features

* add _authenticator setter ([96d975f](https://github.com/PurrNet/PurrNet/commit/96d975fee284229f1f58452535c44d0b3b8e4923))

# [1.20.0-beta.41](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.40...v1.20.0-beta.41) (2026-03-26)


### Bug Fixes

* Statistics manager consistency issues ([bbf883c](https://github.com/PurrNet/PurrNet/commit/bbf883c869788a496ca39c9dd83595826254e21a))

# [1.20.0-beta.40](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.39...v1.20.0-beta.40) (2026-03-26)


### Bug Fixes

* dont use build index for scenes, use path hash instead ([66dfbf0](https://github.com/PurrNet/PurrNet/commit/66dfbf064dde0ef5e9624be9a2d67aeb4b0d1279))

# [1.20.0-beta.39](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.38...v1.20.0-beta.39) (2026-03-26)


### Bug Fixes

* Ensure rigidbody getter ([1a58f57](https://github.com/PurrNet/PurrNet/commit/1a58f57aa75997a157a79d3c73d6756461ac0258))

# [1.20.0-beta.38](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.37...v1.20.0-beta.38) (2026-03-26)


### Features

* allow to discover instantiated network identities based on NetworkRules ([ce50f45](https://github.com/PurrNet/PurrNet/commit/ce50f459dbc22c0373702d21a7b4198f76c0e4f0))

# [1.20.0-beta.37](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.36...v1.20.0-beta.37) (2026-03-26)


### Bug Fixes

* update LiteNetLib to latest (2.1.2) ([1db1f76](https://github.com/PurrNet/PurrNet/commit/1db1f766426c2f75ee46f24c2c299ea40aa766fe))

# [1.20.0-beta.36](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.35...v1.20.0-beta.36) (2026-03-25)


### Bug Fixes

* Statistics manager consistency improvements ([471c569](https://github.com/PurrNet/PurrNet/commit/471c56919c2b69d45a1ebd8d6171fe8a2495c091))

# [1.20.0-beta.35](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.34...v1.20.0-beta.35) (2026-03-25)


### Bug Fixes

* include `udpPortV2` in RelayServer and obsolete the old `udpPort` ([f655009](https://github.com/PurrNet/PurrNet/commit/f655009df270b2fb394233dce54da99bb227c65a))

# [1.20.0-beta.34](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.33...v1.20.0-beta.34) (2026-03-24)


### Bug Fixes

* Added advanced settings for Network Rigidbody ([96dd6fc](https://github.com/PurrNet/PurrNet/commit/96dd6fc954d22b13302741ce061185114745d386))

# [1.20.0-beta.33](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.32...v1.20.0-beta.33) (2026-03-24)


### Bug Fixes

* update litenetlib on relay too ([16071d4](https://github.com/PurrNet/PurrNet/commit/16071d4c68d16b9bfdf1bbae34222ae6b5640b48))

# [1.20.0-beta.32](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.31...v1.20.0-beta.32) (2026-03-24)


### Bug Fixes

* rename namespace for PoolingConfigDrawer to reflect it's editor only ([434bb86](https://github.com/PurrNet/PurrNet/commit/434bb86d26bd5caa286e2c5d2c19b06ac87e6073))

# [1.20.0-beta.31](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.30...v1.20.0-beta.31) (2026-03-23)


### Bug Fixes

* Convert network assets to new unified setup ([1e149b2](https://github.com/PurrNet/PurrNet/commit/1e149b2cfcf2697d9b99d958264969ef782c44e9))
* Convert network prefabs + addressables to new unified setup ([cad954a](https://github.com/PurrNet/PurrNet/commit/cad954ab69a927cd70bbbdd5fa9f116563e66f32))
* Improved addressable network prefabs design unification ([d2c0b01](https://github.com/PurrNet/PurrNet/commit/d2c0b01546df3679c56f76e0bd74d59a449ae1c0))

# [1.20.0-beta.30](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.29...v1.20.0-beta.30) (2026-03-23)


### Bug Fixes

* Added addressables proxy ([8ddc200](https://github.com/PurrNet/PurrNet/commit/8ddc20058cfa3b13690d5486cbb94d061692c845))
* Added purrlogger support ([07a2cb4](https://github.com/PurrNet/PurrNet/commit/07a2cb41ecb9ea364672b90e94c8d377496ba7fd))
* Allocation optimization of addressables setup ([78108f1](https://github.com/PurrNet/PurrNet/commit/78108f16a2e4a9ee94fc1932fe7ec2cdbfe89fdb))
* avoid spamming editor with patch attempts; only do it for clone editors. ([1912c8d](https://github.com/PurrNet/PurrNet/commit/1912c8d7692d2e8ea15b44a8b63cdfd3bf66b711))
* Delete Assets/PurrNet/Externals/LiteNetLib/LiteNetLib.csproj.meta ([ad616c9](https://github.com/PurrNet/PurrNet/commit/ad616c92e0d7944c2d3e7134b16b6d3d0f78f04e))
* increase MTU margin for delta module ([ceb45c7](https://github.com/PurrNet/PurrNet/commit/ceb45c78378a6f535ab9c218903f007165c829ea))
* Potential addressables spawning fix ([0de22ac](https://github.com/PurrNet/PurrNet/commit/0de22ac008deef3a747f6a405ee47eea4b1cfc28))
* remove compression for the delta packet, mixed with delta compression it tends to create bigger packets due to high entropy already ([052a222](https://github.com/PurrNet/PurrNet/commit/052a222249662eb45fcff4965a43f39c5ccd0bad))
* update LiteNetLib ([7f1f348](https://github.com/PurrNet/PurrNet/commit/7f1f3488ab0d97573dae7673bc333bbe6a02ee74))
* use our enumerator for DisposableDictionary for determinism reasons ([7dda783](https://github.com/PurrNet/PurrNet/commit/7dda783e522f2d6e986172daf624b07154ccddc1))
* UTP server odd symbols ([1991e0c](https://github.com/PurrNet/PurrNet/commit/1991e0cd0cf99193b64456ae75831299ec05a9ce))

# [1.20.0-beta.29](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.28...v1.20.0-beta.29) (2026-03-22)


### Bug Fixes

* Improved NetworkRB Support for Unity 5 & 6 ([79b09c8](https://github.com/PurrNet/PurrNet/commit/79b09c8bf8a851ebfb0cda8f2296f02401dc8be2))

# [1.20.0-beta.28](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.27...v1.20.0-beta.28) (2026-03-22)


### Bug Fixes

* Improve host consistency for Network Rigidbody ([6bfef4b](https://github.com/PurrNet/PurrNet/commit/6bfef4b44e6d4f50b35b6fc91a70a5f37c92c49c))

# [1.20.0-beta.27](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.26...v1.20.0-beta.27) (2026-03-22)


### Bug Fixes

* potential collision fix ([f5f346a](https://github.com/PurrNet/PurrNet/commit/f5f346ab1cd63caa5dc55a373178b92fc5e92075))

# [1.20.0-beta.26](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.25...v1.20.0-beta.26) (2026-03-22)


### Bug Fixes

* make the masterServer of PurrTransport.cs editable through a property ([4b609da](https://github.com/PurrNet/PurrNet/commit/4b609da14be11ba8d7559eaf4dda907fff5189a9))

# [1.20.0-beta.25](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.24...v1.20.0-beta.25) (2026-03-22)


### Bug Fixes

* Stop correction range from utilizing velocity damping ([eff1b2b](https://github.com/PurrNet/PurrNet/commit/eff1b2b5bc95a727dce9a27021ecc4d1056db24e))

# [1.20.0-beta.24](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.23...v1.20.0-beta.24) (2026-03-22)


### Bug Fixes

* Add prediction factor + more correction settings ([072b368](https://github.com/PurrNet/PurrNet/commit/072b3688d3b5569aecb9fef33e582d91f3fc7335))
* NetworkRigidbody ring buffer rework ([b55cd60](https://github.com/PurrNet/PurrNet/commit/b55cd60137e250568bc9bfa5569b2dc95779108f))

# [1.20.0-beta.23](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.22...v1.20.0-beta.23) (2026-03-20)


### Bug Fixes

* Async packing change ([ef12d66](https://github.com/PurrNet/PurrNet/commit/ef12d66eb7348868b06a1912933aca9d63bfd84c))

# [1.20.0-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.21...v1.20.0-beta.22) (2026-03-20)


### Bug Fixes

* more resilience to _keys being corrupted ([9376e04](https://github.com/PurrNet/PurrNet/commit/9376e0420fc2ce7650ad6649efe04af2e26285ce))

# [1.20.0-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.20...v1.20.0-beta.21) (2026-03-20)


### Bug Fixes

* deffensively only add keys when .Add is successful ([9781f6c](https://github.com/PurrNet/PurrNet/commit/9781f6ca55666ce9f46b9f4fcf1e3c54268a9f4d))

# [1.20.0-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.19...v1.20.0-beta.20) (2026-03-20)


### Bug Fixes

* clear old values beofre adding them ([f803588](https://github.com/PurrNet/PurrNet/commit/f80358880682ff2eb1a505146fbc4d79fba04394))

# [1.20.0-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.18...v1.20.0-beta.19) (2026-03-18)


### Bug Fixes

* introcude pre/post scene unloaded events ([8c15a55](https://github.com/PurrNet/PurrNet/commit/8c15a55b5746125d8a676da10dab0320b6718b1a))

# [1.20.0-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.17...v1.20.0-beta.18) (2026-03-18)


### Bug Fixes

* animation reconcile when someone becomes an observer ([a3a9d92](https://github.com/PurrNet/PurrNet/commit/a3a9d929d694ae99f1f5e02589f34123fd640f01))

# [1.20.0-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.16...v1.20.0-beta.17) (2026-03-18)


### Features

* configurable MTU exceeded behaviour for unreliable channels ([1f58143](https://github.com/PurrNet/PurrNet/commit/1f581437e6c8ea9f26580a591ec2f5ce57bd1c89))

# [1.20.0-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.15...v1.20.0-beta.16) (2026-03-17)


### Bug Fixes

* package manager compiler error and extra UI updates ([6b930b3](https://github.com/PurrNet/PurrNet/commit/6b930b36698986fe7bb9daac199d08e5ed7e5a58))

# [1.20.0-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.14...v1.20.0-beta.15) (2026-03-16)


### Bug Fixes

* allow PurrTransport to use the new Pipe mode (relay updated) ([2c36c6e](https://github.com/PurrNet/PurrNet/commit/2c36c6ef94eeb97275c37c33debeae556a4f4fdb))

# [1.20.0-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.13...v1.20.0-beta.14) (2026-03-14)


### Bug Fixes

* do memcmp for unmanaged types when checking equality ([fea1133](https://github.com/PurrNet/PurrNet/commit/fea1133386b821cbe99330aa614de17389e1731d))

# [1.20.0-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.12...v1.20.0-beta.13) (2026-03-14)


### Bug Fixes

* improve some UDP delta timings and no need to recycle since AutoRecycle is enabled ([8027850](https://github.com/PurrNet/PurrNet/commit/80278508cee23fb591e52d7b4fad01fcf639af6f))

# [1.20.0-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.11...v1.20.0-beta.12) (2026-03-13)


### Bug Fixes

* Sync array ownership fixes ([04f2f3d](https://github.com/PurrNet/PurrNet/commit/04f2f3dcf61b7633883fe5aaf69352c0c6584149))

# [1.20.0-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.10...v1.20.0-beta.11) (2026-03-12)


### Bug Fixes

* shortcut loops and other expenssive lookups early if Statistics.shouldTrack is false ([e6fb515](https://github.com/PurrNet/PurrNet/commit/e6fb515a1f9b73192110e70b11e8faec64cd4807))

# [1.20.0-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.9...v1.20.0-beta.10) (2026-03-12)


### Bug Fixes

* avoid reconstructing the targets list every time an RPC is trying to send, instead send instructions to filter the full list ([cf088db](https://github.com/PurrNet/PurrNet/commit/cf088dbb9efca4b06cd2a64a9a0476f957322fb6))
* same for static RPCs ([0a0b8eb](https://github.com/PurrNet/PurrNet/commit/0a0b8eb08a72c190334f9572d24a807e5b40153e))

# [1.20.0-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.8...v1.20.0-beta.9) (2026-03-12)


### Bug Fixes

* packer improvements (cpu), and some tests (benchmarks) ([63ca673](https://github.com/PurrNet/PurrNet/commit/63ca673ad01985e683788137fac5ebd5cbbdb8c7))

# [1.20.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.7...v1.20.0-beta.8) (2026-03-10)


### Bug Fixes

* Improved network reflection method handling ([c2128bd](https://github.com/PurrNet/PurrNet/commit/c2128bdf26fee22834af9430c689c15195c7a61b))

# [1.20.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.6...v1.20.0-beta.7) (2026-03-10)


### Features

* add network simulation settings for UDP transport (LiteNetLib specific) ([ceed9dd](https://github.com/PurrNet/PurrNet/commit/ceed9dd18d3f3c99495a3662575e44848fd0b490))

# [1.20.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.5...v1.20.0-beta.6) (2026-03-10)


### Bug Fixes

* Implemented target smoothing for Network RB ([00c7afd](https://github.com/PurrNet/PurrNet/commit/00c7afdeaae8186c44acad010333ef2a3f93d02a))

# [1.20.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.4...v1.20.0-beta.5) (2026-03-09)


### Bug Fixes

* Improved client interpolation of network rigidbody ([7038ce5](https://github.com/PurrNet/PurrNet/commit/7038ce58c0a9fb818d05055c852bf085da177e66))

# [1.20.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.3...v1.20.0-beta.4) (2026-03-09)


### Bug Fixes

* Made RB sleep optional ([653fbda](https://github.com/PurrNet/PurrNet/commit/653fbdad0b30187ef75581da228ec0732dee84a5))

# [1.20.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.2...v1.20.0-beta.3) (2026-03-09)


### Bug Fixes

* Parrelsync clone reflection fix ([14cc0aa](https://github.com/PurrNet/PurrNet/commit/14cc0aab68e9cef082d52505e36dbcdecd3f157c))
* Warning for Unity namespace ([75cc2f0](https://github.com/PurrNet/PurrNet/commit/75cc2f0756c41021f27891fa051ed486334966b3))

# [1.20.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.20.0-beta.1...v1.20.0-beta.2) (2026-03-09)


### Bug Fixes

* Utilize caching for network reflection prefabs ([ecbf92d](https://github.com/PurrNet/PurrNet/commit/ecbf92d315910950e64a4ab51821dc25db3d11ed))

# [1.20.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.28...v1.20.0-beta.1) (2026-03-09)


### Features

* enhance authentication version mismatch handling with configurable behavior ([48d222e](https://github.com/PurrNet/PurrNet/commit/48d222e9859275557b56eb9a9d28e382deb00649))

## [1.19.2-beta.28](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.27...v1.19.2-beta.28) (2026-03-09)


### Bug Fixes

* implement IPurrEquatable interface and related equality helpers (to avoid breaking packages that rely on default c# equality) ([9b748c7](https://github.com/PurrNet/PurrNet/commit/9b748c7dd0a7f7d8cdb536b0edbec0802b9b960e))

## [1.19.2-beta.27](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.26...v1.19.2-beta.27) (2026-03-09)


### Bug Fixes

* correct variable name for RPC key retrieval in RPCModule ([6ac9140](https://github.com/PurrNet/PurrNet/commit/6ac914042aa287561d21b649aff42a1c3a3bd199))

## [1.19.2-beta.26](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.25...v1.19.2-beta.26) (2026-03-09)


### Bug Fixes

* bobsi shenanigans ([b68b786](https://github.com/PurrNet/PurrNet/commit/b68b78666d42f1c8184f78cdfcc5c4d54e556cc6))

## [1.19.2-beta.25](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.24...v1.19.2-beta.25) (2026-03-07)


### Bug Fixes

* Network rigidbody stops acting when disabled ([a44a742](https://github.com/PurrNet/PurrNet/commit/a44a74220313f4cd1872702cd523e8a34c65d736))

## [1.19.2-beta.24](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.23...v1.19.2-beta.24) (2026-03-07)


### Bug Fixes

* Utilize ValueTask to avoid GC on synchronous completion ([7e552f2](https://github.com/PurrNet/PurrNet/commit/7e552f22f9c53e6b9ea11578091f5395bb4fef07))

## [1.19.2-beta.23](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.22...v1.19.2-beta.23) (2026-03-07)


### Bug Fixes

* Added Network Addressables helper struct ([7ab34ad](https://github.com/PurrNet/PurrNet/commit/7ab34addd2d0f6a737875ed12a3b11f5c2b4b961))

## [1.19.2-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.21...v1.19.2-beta.22) (2026-03-05)


### Bug Fixes

* PurrNet Packages: Implement user authentication and profile management ([02089cd](https://github.com/PurrNet/PurrNet/commit/02089cd937d10148962345903e1c9d82c795281f))

## [1.19.2-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.20...v1.19.2-beta.21) (2026-03-04)


### Bug Fixes

* Added callbacks for addressable scene loading ([01a55c9](https://github.com/PurrNet/PurrNet/commit/01a55c94424d88ca43626bf8f54731332b720a21))

## [1.19.2-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.19...v1.19.2-beta.20) (2026-03-04)


### Bug Fixes

* Added server event for addressable loading on player ([adf6ad9](https://github.com/PurrNet/PurrNet/commit/adf6ad97ddc674176917845065b4ecfab28f554c))
* Getter for pending addressable scene operations ([377e11d](https://github.com/PurrNet/PurrNet/commit/377e11d639ac50bcf72fbdf1e253b1dc4e141209))
* Helper for addressable loading ([46087e2](https://github.com/PurrNet/PurrNet/commit/46087e25b9adee4f26478abcf6de441100df0d84))
* More helpers for addressables loading ([b278abc](https://github.com/PurrNet/PurrNet/commit/b278abc6aac6cfc299a45541b77d425d77560061))

## [1.19.2-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.18...v1.19.2-beta.19) (2026-03-04)


### Bug Fixes

* Package manager shortcut + prefer git urls ([bdf2fcb](https://github.com/PurrNet/PurrNet/commit/bdf2fcbd5b97adad784a2622a71650b30b69188f))

## [1.19.2-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.17...v1.19.2-beta.18) (2026-03-04)


### Bug Fixes

* PurrNet Packages: allow to update all packages at once ([ecd3291](https://github.com/PurrNet/PurrNet/commit/ecd32918d4a2b36c7d3f84b8e242f0e1d3c5d318))

## [1.19.2-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.16...v1.19.2-beta.17) (2026-03-04)


### Bug Fixes

* compares all package.json entries and picks the shallowest one (shortest prefix) instead of ([f5decec](https://github.com/PurrNet/PurrNet/commit/f5dececb4f5888a4f7fec30a53791adb24ec8e9a))

## [1.19.2-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.15...v1.19.2-beta.16) (2026-03-04)


### Bug Fixes

* rename PurrNet Package Manager to PurrNet Packages and fix some bugs ([a346469](https://github.com/PurrNet/PurrNet/commit/a34646958f183ab4a693226d3d10057766461d40))

## [1.19.2-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.14...v1.19.2-beta.15) (2026-03-04)


### Bug Fixes

* Addressable scene loading for reconnecting ([120c26e](https://github.com/PurrNet/PurrNet/commit/120c26ec7b32ec64bd22e5fa71060860d6ff7538))

## [1.19.2-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.13...v1.19.2-beta.14) (2026-03-03)


### Bug Fixes

* update bone entry size calculations for accurate MTU handling in NetworkBones ([9ae2cde](https://github.com/PurrNet/PurrNet/commit/9ae2cde5ba0d019569afe0f8e8dd085dd83a9253))

## [1.19.2-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.12...v1.19.2-beta.13) (2026-03-03)


### Bug Fixes

* invoke InternalTick in NetworkIdentity when client is not registered ([c164ee0](https://github.com/PurrNet/PurrNet/commit/c164ee011c8c548496a9dedef0be4f0f1473930e))

## [1.19.2-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.11...v1.19.2-beta.12) (2026-03-02)


### Bug Fixes

* optimize owner connection checks and caching in NetworkBones ([436f332](https://github.com/PurrNet/PurrNet/commit/436f33271b2eef290a09bd9e395771cb5bc06355))

## [1.19.2-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.10...v1.19.2-beta.11) (2026-03-02)


### Bug Fixes

* enhance bone info and delta module with key hash caching for improved performance ([ab264f0](https://github.com/PurrNet/PurrNet/commit/ab264f0417ffe00a390c10ec64347f5f9cc2a4ad))

## [1.19.2-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.9...v1.19.2-beta.10) (2026-03-02)


### Bug Fixes

* remove redundant empty response sending in RPC error handling ([692af16](https://github.com/PurrNet/PurrNet/commit/692af167bbf05ac51675b6d1f98f7f41c83443e6))

## [1.19.2-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.8...v1.19.2-beta.9) (2026-03-02)


### Bug Fixes

* refactoring and fixing async RPC issues ([6957af5](https://github.com/PurrNet/PurrNet/commit/6957af5f1e3e3d3de131d9c37cc47d0cc3a43783))

## [1.19.2-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.7...v1.19.2-beta.8) (2026-03-02)


### Bug Fixes

* Try to avoid missing scripts causing editor spam ([d36ef20](https://github.com/PurrNet/PurrNet/commit/d36ef206b246f395fe5eaeeddb7817193eb83f3b))

## [1.19.2-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.6...v1.19.2-beta.7) (2026-03-01)


### Bug Fixes

* Added methods to network reflection ([8ada03c](https://github.com/PurrNet/PurrNet/commit/8ada03c293326820a0f92ce3542a57c0e4e8e3b9))
* Safety for network identity inspector ([0a794eb](https://github.com/PurrNet/PurrNet/commit/0a794eb229a815b6ec6d3c901cf6b3288af020a2))

## [1.19.2-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.5...v1.19.2-beta.6) (2026-03-01)


### Bug Fixes

* Proper serialization of custom struct and classes ([249da35](https://github.com/PurrNet/PurrNet/commit/249da35a6ceb3d6d2097882a72b2ed719bdf5ffa))

## [1.19.2-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.4...v1.19.2-beta.5) (2026-03-01)


### Bug Fixes

* Processor change for IL2 build issues ([fdf4f53](https://github.com/PurrNet/PurrNet/commit/fdf4f534e6fa5e532c8925960bf30c15992a2156))

## [1.19.2-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.3...v1.19.2-beta.4) (2026-02-28)


### Bug Fixes

* BitPacker reinforce reading to avoid reading out of bounds ([84c655c](https://github.com/PurrNet/PurrNet/commit/84c655cbae4ea063232290583d0232670188bac2))

## [1.19.2-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.2...v1.19.2-beta.3) (2026-02-28)


### Bug Fixes

* define symbols for UTP package ([639ccfa](https://github.com/PurrNet/PurrNet/commit/639ccfa88efba3e29962e8ac15ecc5a10494cd64))

## [1.19.2-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.19.2-beta.1...v1.19.2-beta.2) (2026-02-27)


### Bug Fixes

* icons failing to be reimported in newer versions ([fa971b8](https://github.com/PurrNet/PurrNet/commit/fa971b8668b14289fdb24aed2a016b272a018611))

## [1.19.2-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.19.1...v1.19.2-beta.1) (2026-02-26)


### Bug Fixes

* update MTU retrieval logic in PlayersManager and optimize byte length calculation in RPCBatch ([e35d7b4](https://github.com/PurrNet/PurrNet/commit/e35d7b42a8de9e2a91665ab0e3cb7dfb42bed804))

## [1.19.1](https://github.com/PurrNet/PurrNet/compare/v1.19.0...v1.19.1) (2026-02-25)


### Bug Fixes

* add isLocalPlayerReady property and enhance owner change logging ([d42bfa9](https://github.com/PurrNet/PurrNet/commit/d42bfa98101df8e1435d1f0c07caf37c163a764e))
* clear exception handlers in PostProcessor to ensure clean IL processing ([d33c4f9](https://github.com/PurrNet/PurrNet/commit/d33c4f9b8adf291ca76d9ee2999f8d18aaf0b1cc))
* convert short form jumps to long form to prevent overflow ([b5d6e80](https://github.com/PurrNet/PurrNet/commit/b5d6e808293d48a58d31c4f99f67f4edc4bd631d))
* enhance delegate handling in NativeDeltaPacker and NativePacker for better compatibility ([c3f8bc0](https://github.com/PurrNet/PurrNet/commit/c3f8bc0c827b72296a7cdf469f14ce0638201b4f))
* enhance error handling in ProcessSpawnWhenLoadedAsync ([d3ec7b5](https://github.com/PurrNet/PurrNet/commit/d3ec7b5403f89d3dcc3e24ef91819abd1c046400))
* ensure directory creation for version file in OnPreprocessBuild ([82dd9a7](https://github.com/PurrNet/PurrNet/commit/82dd9a738f406adf5188d18844f3fa9d22f9108b))
* ensure parent is not null before checking controller status ([58b299e](https://github.com/PurrNet/PurrNet/commit/58b299e1b17639e6c2d5448694f3e00ba25be465))
* if network module RPC fails, include some extra context ([375a26e](https://github.com/PurrNet/PurrNet/commit/375a26e3451198b98b5b8d7ca920823d8480a710))
* implement MTU debugging and clean up unused SendRaw methods ([d2f872d](https://github.com/PurrNet/PurrNet/commit/d2f872dabf8030f95f0b58c6d82fe9ec33488c88))
* improve error messages in Hasher and update null check in NetworkManager ([c5977ef](https://github.com/PurrNet/PurrNet/commit/c5977efb6f86711a47d408ff4d3cd1de0e586dfe))
* refactor hashing for better compatibility across builds and platforms ([1de34d2](https://github.com/PurrNet/PurrNet/commit/1de34d282691f7c508cf0bba7222659dfc45d32c))
* replace CalculateHashes with LoadOrGenerateHashes in NetworkManager and related tests ([237a628](https://github.com/PurrNet/PurrNet/commit/237a6280650c267fb9042a18222d12af3b9e718b))
* replace MethodHandle with Marshal.GetFunctionPointerForDelegate for delegate compatibility ([246c242](https://github.com/PurrNet/PurrNet/commit/246c242f689d715986b30ef42e8fbcb1a7311226))
* replace PackedUInt with uint for typeHash consistency across structures ([df45960](https://github.com/PurrNet/PurrNet/commit/df4596012cf3f145b4a7f7438caabb8bd489d87e))

## [1.19.1-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.19.1-beta.10...v1.19.1-beta.11) (2026-02-25)


### Bug Fixes

* clear exception handlers in PostProcessor to ensure clean IL processing ([d33c4f9](https://github.com/PurrNet/PurrNet/commit/d33c4f9b8adf291ca76d9ee2999f8d18aaf0b1cc))

## [1.19.1-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.19.1-beta.9...v1.19.1-beta.10) (2026-02-25)


### Bug Fixes

* convert short form jumps to long form to prevent overflow ([b5d6e80](https://github.com/PurrNet/PurrNet/commit/b5d6e808293d48a58d31c4f99f67f4edc4bd631d))

## [1.19.1-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.19.1-beta.8...v1.19.1-beta.9) (2026-02-25)


### Bug Fixes

* enhance delegate handling in NativeDeltaPacker and NativePacker for better compatibility ([c3f8bc0](https://github.com/PurrNet/PurrNet/commit/c3f8bc0c827b72296a7cdf469f14ce0638201b4f))

## [1.19.1-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.19.1-beta.7...v1.19.1-beta.8) (2026-02-25)


### Bug Fixes

* ensure directory creation for version file in OnPreprocessBuild ([82dd9a7](https://github.com/PurrNet/PurrNet/commit/82dd9a738f406adf5188d18844f3fa9d22f9108b))
* refactor hashing for better compatibility across builds and platforms ([1de34d2](https://github.com/PurrNet/PurrNet/commit/1de34d282691f7c508cf0bba7222659dfc45d32c))
* replace MethodHandle with Marshal.GetFunctionPointerForDelegate for delegate compatibility ([246c242](https://github.com/PurrNet/PurrNet/commit/246c242f689d715986b30ef42e8fbcb1a7311226))
* replace PackedUInt with uint for typeHash consistency across structures ([df45960](https://github.com/PurrNet/PurrNet/commit/df4596012cf3f145b4a7f7438caabb8bd489d87e))

## [1.19.1-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.19.1-beta.6...v1.19.1-beta.7) (2026-02-24)


### Bug Fixes

* enhance error handling in ProcessSpawnWhenLoadedAsync ([d3ec7b5](https://github.com/PurrNet/PurrNet/commit/d3ec7b5403f89d3dcc3e24ef91819abd1c046400))

## [1.19.1-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.19.1-beta.5...v1.19.1-beta.6) (2026-02-24)


### Bug Fixes

* implement MTU debugging and clean up unused SendRaw methods ([d2f872d](https://github.com/PurrNet/PurrNet/commit/d2f872dabf8030f95f0b58c6d82fe9ec33488c88))

## [1.19.1-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.19.1-beta.4...v1.19.1-beta.5) (2026-02-24)


### Bug Fixes

* replace CalculateHashes with LoadOrGenerateHashes in NetworkManager and related tests ([237a628](https://github.com/PurrNet/PurrNet/commit/237a6280650c267fb9042a18222d12af3b9e718b))

## [1.19.1-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.19.1-beta.3...v1.19.1-beta.4) (2026-02-24)


### Bug Fixes

* improve error messages in Hasher and update null check in NetworkManager ([c5977ef](https://github.com/PurrNet/PurrNet/commit/c5977efb6f86711a47d408ff4d3cd1de0e586dfe))

## [1.19.1-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.19.1-beta.2...v1.19.1-beta.3) (2026-02-22)


### Bug Fixes

* add isLocalPlayerReady property and enhance owner change logging ([d42bfa9](https://github.com/PurrNet/PurrNet/commit/d42bfa98101df8e1435d1f0c07caf37c163a764e))

## [1.19.1-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.19.1-beta.1...v1.19.1-beta.2) (2026-02-22)


### Bug Fixes

* ensure parent is not null before checking controller status ([58b299e](https://github.com/PurrNet/PurrNet/commit/58b299e1b17639e6c2d5448694f3e00ba25be465))

## [1.19.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.19.0...v1.19.1-beta.1) (2026-02-22)


### Bug Fixes

* if network module RPC fails, include some extra context ([375a26e](https://github.com/PurrNet/PurrNet/commit/375a26e3451198b98b5b8d7ca920823d8480a710))

# [1.19.0](https://github.com/PurrNet/PurrNet/compare/v1.18.0...v1.19.0) (2026-02-21)


### Bug Fixes

* ActualGetRelayServersAsync sometimes would come with an empty string and throw and exception ([c186213](https://github.com/PurrNet/PurrNet/commit/c1862139fb17591e678acfc40a9ded9886b5ee83))
* add aggressive inlining to Duplicate method for JIT performance improvement ([f46516e](https://github.com/PurrNet/PurrNet/commit/f46516e8aafcd64759874fd3546148bb5cc06ce4))
* add bounds checking to BitPacker.ReadBits to prevent native crash on malformed packets ([77abf0d](https://github.com/PurrNet/PurrNet/commit/77abf0dd8d177cf0fc0712aec7eda284c17e0af0))
* add deterministic hash to the packer ([ceaaf4d](https://github.com/PurrNet/PurrNet/commit/ceaaf4d08db74443a67bdadf533bcd5d955d2c90))
* add null check for networkManager in PlayersManager retrieval ([567ac4f](https://github.com/PurrNet/PurrNet/commit/567ac4fb1e9022c774c843635db63d1c41c5fe86))
* Added addressables scenes support ([e35670b](https://github.com/PurrNet/PurrNet/commit/e35670b50a8fe08593daa088b315eee9f6767758))
* Added dynamic force correction to network RB ([c6a27cf](https://github.com/PurrNet/PurrNet/commit/c6a27cfbac65ec379c33c88bc8d939091a2b4ed7))
* Added state machine current state helpers ([6ebfff9](https://github.com/PurrNet/PurrNet/commit/6ebfff9a934aadbf3e0c3010fff2632751e3538d))
* adjust MTU calculation to account for maximum bone entry size ([add7ebc](https://github.com/PurrNet/PurrNet/commit/add7ebcf7cab85b6b5aa8ad921620876ebd74114))
* allow to replace networkAssets at runtime ([d3dcd69](https://github.com/PurrNet/PurrNet/commit/d3dcd6924e1e87f43659542e20475d1e1cbba8fd))
* array comparison ([a278892](https://github.com/PurrNet/PurrNet/commit/a278892d44e3ca79d30a9d5becdc5469f07cff52))
* Backwards compatibility for network RB ([94af929](https://github.com/PurrNet/PurrNet/commit/94af9293ed711a98dfea2a83f35437ab285fc232))
* BadImageFormatException: Method with open type while not compiling gshared ([2216218](https://github.com/PurrNet/PurrNet/commit/2216218a13f64507d2875b7691980251846ef8c4))
* Basic addressables spawning and despawning through network manager ([50a6c20](https://github.com/PurrNet/PurrNet/commit/50a6c204bfc6fcd159ef50166d29492ba453fd74))
* before failing to set syncvar double check if cached value is up to date; once client does a change to the syncvar, ingore server catchups ([f2cc835](https://github.com/PurrNet/PurrNet/commit/f2cc835a86300263197632ba5709938f4683b5b5))
* buffer overflow in RPC response handling ([6a83e07](https://github.com/PurrNet/PurrNet/commit/6a83e07e0d1ce1fbf525fd36ad57df6f3d38e455))
* cleanup server/client modules, this caused some issues when next playthrough starting mode was changed ([65b2d0c](https://github.com/PurrNet/PurrNet/commit/65b2d0c6a330514edfb40d28c37cb75716b9a00d))
* compiler errors ([ae009e6](https://github.com/PurrNet/PurrNet/commit/ae009e64a10965bf78bdfd6a49d470337a94626f))
* Composite Prefab Provider for NM ([c74f155](https://github.com/PurrNet/PurrNet/commit/c74f155c61c2a385d0c6258a722a4c4260d0cf96))
* delta module proper MTU usage instead of hard coded value ([6e9a1ef](https://github.com/PurrNet/PurrNet/commit/6e9a1efd41878cc010930a1ca1ed4be5e2118c6f))
* DisposableArray duplicate ([af07f84](https://github.com/PurrNet/PurrNet/commit/af07f84b4d71d01a665aa797e71e58502ed11be9))
* dont include rpc related things here ([f986586](https://github.com/PurrNet/PurrNet/commit/f98658604cdcc4266b52f090ce97c57e50d0ff12))
* dont use static constructor ([0ad1ced](https://github.com/PurrNet/PurrNet/commit/0ad1ced6c34502bcfe91c298b7fe08fb250422ee))
* dynamically changing region was error prone for the purrtransport ([92e2bb0](https://github.com/PurrNet/PurrNet/commit/92e2bb079091a474b5397327a7efe1e812e62235))
* ensure Awake is called before registering modules ([0b07224](https://github.com/PurrNet/PurrNet/commit/0b072244d039609817343b8a62ffd9b3ceca5785))
* expose latest read data properties for network transform ([65e6a23](https://github.com/PurrNet/PurrNet/commit/65e6a236b5819fec9e74002c886b245014713bbe))
* fuck you c# ([d1986df](https://github.com/PurrNet/PurrNet/commit/d1986df12b414c59e1f81861677a7dfffa90b996))
* GetHashCode fails if list is null ([0ab9281](https://github.com/PurrNet/PurrNet/commit/0ab9281b48dce6cb9124dc7a6a601fbb7e7880eb))
* GetModule can fail here ([4ea9c5a](https://github.com/PurrNet/PurrNet/commit/4ea9c5ab6133cf14deace076f556649f78619cb6))
* give each thread it's own bit packet pool ([0d5dd6c](https://github.com/PurrNet/PurrNet/commit/0d5dd6c40209cccf593cb880592a2c75a7ae4fb5))
* handle exceptions during tick processing in ServerTick method ([78f1408](https://github.com/PurrNet/PurrNet/commit/78f14086bede0a9411ec507e4f47edd191ad1ee3))
* handle null selfRef and improve error handling in IEquatable generation ([44166b5](https://github.com/PurrNet/PurrNet/commit/44166b5a6d826afadcbb85c7d7f3af80586c1e54))
* hash collisions would break delta compression, added type explicitly to avoid this ([bc670ab](https://github.com/PurrNet/PurrNet/commit/bc670ab69682922c31c05fefae40c16f19f4f02f))
* Host migration should be enabled to check if it must force CanSee or not ([5814337](https://github.com/PurrNet/PurrNet/commit/58143374834ae94d908a8d552a6150d45bd45c00))
* Host migration should be enabled to check if it must force scene public or not. ([576e263](https://github.com/PurrNet/PurrNet/commit/576e263412f790af8c161fe1993d2fddbac39ebc))
* if transform from A to B but types mismatch create it from scratch ([2e1799c](https://github.com/PurrNet/PurrNet/commit/2e1799c7bb5a3e4f78459093207f60601db21fa1))
* improve network assets reliability and synchronization ([c508cd1](https://github.com/PurrNet/PurrNet/commit/c508cd10b733aa9ae841ad7b9a54712766b1a8fe))
* Improved GC of statistics manager ([1d563a8](https://github.com/PurrNet/PurrNet/commit/1d563a8234393851ac2bdfdac9df6a80250a821a))
* Improved Unity 5 support ([d228ee1](https://github.com/PurrNet/PurrNet/commit/d228ee1dc9ee91f73e743d03fba210a4eb32c723))
* initialize ping history size and stats on server connection ([5f8689a](https://github.com/PurrNet/PurrNet/commit/5f8689ae9ae785e2764725bb7f53a0c6ed018aa2))
* instead of throwing an exception lets just log what happened ([4a1c0b8](https://github.com/PurrNet/PurrNet/commit/4a1c0b8047d9ca08016a6f028a35cdf639a16483))
* InvokeLocal wasn't reseting position properly all the time ([66c8536](https://github.com/PurrNet/PurrNet/commit/66c8536ecde5bba6010104f7039cfab3e528a48c))
* make sure we account for local client being gone when handling ticks ([ca622e5](https://github.com/PurrNet/PurrNet/commit/ca622e51372b387b4f687d577341bfcd5f2223a5))
* myers diff GC fixes and some tests for consistency ([6fc9525](https://github.com/PurrNet/PurrNet/commit/6fc95257d3aa4d0c2651b68e39e0a17d2bca8583))
* notify user if modules can't be registered properly ([1f002e9](https://github.com/PurrNet/PurrNet/commit/1f002e9c50671a51605ab8f5d886053073c2485d))
* packer duplicate mistake; tests for hasher ([b66ca76](https://github.com/PurrNet/PurrNet/commit/b66ca76f528746e4aa126d90948482ec26e481e6))
* pass asServer parameter to TickManager in NetworkManager and RawNetManager ([0c288b3](https://github.com/PurrNet/PurrNet/commit/0c288b332028caf1f937014201f78ec9f5e8d688))
* prevent destruction of objects in editor mode and cancel if it was already destroyed ([5351ba5](https://github.com/PurrNet/PurrNet/commit/5351ba52cd4d0569c9c1c05b349f83dd583a6351))
* properly call despawn() even if not fully spawned ([0b1739f](https://github.com/PurrNet/PurrNet/commit/0b1739feb6aba7e3eccef27d410898c4a435aeaf))
* Push for version change ([2cb71d5](https://github.com/PurrNet/PurrNet/commit/2cb71d513db1f3540f38c9e9ffb913f3c3ff37ad))
* Quaternion equality check is stupid ([69b1c32](https://github.com/PurrNet/PurrNet/commit/69b1c32d766429307ce0d2b277c45994c53fd838))
* remove commented-out code in PlayModePatch ([d1f866c](https://github.com/PurrNet/PurrNet/commit/d1f866c2a0c640764578a12c99b0a6241741492e))
* reworking bit packer functions ([0d6ca93](https://github.com/PurrNet/PurrNet/commit/0d6ca93638842268429fbccccb2ae397ec02ca12))
* Scaling hard correction threshold of Network RB ([39d5cb2](https://github.com/PurrNet/PurrNet/commit/39d5cb21b1ffba2c0db9e2210eabfe40b5fec525))
* scene module events not firing ([382094f](https://github.com/PurrNet/PurrNet/commit/382094f8ed40c82c556532dedfd133cd9c3bbd90))
* Small miss on the inspector ([0df0d5c](https://github.com/PurrNet/PurrNet/commit/0df0d5c22ac515243454f9a05d123b39047d3a95))
* Smooth rotational syncing on Network RB ([a4ca898](https://github.com/PurrNet/PurrNet/commit/a4ca898e5372365cf26f3c30ec968c1c6354891c))
* Solve inconsistency in sync timing ([59e7993](https://github.com/PurrNet/PurrNet/commit/59e7993fbdf64b5cee42e32b19593766e6d377bc))
* some safety when cleaning client state ([f7011af](https://github.com/PurrNet/PurrNet/commit/f7011afaba24e90fdf80eb90a0509a274c68d1f4))
* Static observer RPC was failing due to bad Send function; removed Raw variants due to easy confusion point ([f7fc861](https://github.com/PurrNet/PurrNet/commit/f7fc8610e6c25c894cc6db5da7143f6fc74b79d4))
* still cull rpcs when player isnt observer for other channels ([69c2677](https://github.com/PurrNet/PurrNet/commit/69c26771081843302480415c118fe556351b8cf7))
* Sync dictionary serialization upgrade ([5934acb](https://github.com/PurrNet/PurrNet/commit/5934acbf9a52511353941040a1643728d658a75f))
* syncvar invalidate is controller earlier ([2444436](https://github.com/PurrNet/PurrNet/commit/2444436aded533525be1e33930dab28a367e8cc2))
* target RPC to host's client failed, this was a regression ([6fdce17](https://github.com/PurrNet/PurrNet/commit/6fdce17673990deb758bd2d4b2348a00ad16c9dd))
* update isControllingSyncVar logic to handle server updates correctly ([520e587](https://github.com/PurrNet/PurrNet/commit/520e587008e80341b6adde704e0af9dec87a172f))
* using after free ([890a29c](https://github.com/PurrNet/PurrNet/commit/890a29cd72bf96a70665da77c37aca34caaaa187))
* Whoopsie ([d2ec215](https://github.com/PurrNet/PurrNet/commit/d2ec2152b1da82740e6d7042b197f14f800cd151))
* wrong local variable index ([612cbfd](https://github.com/PurrNet/PurrNet/commit/612cbfd0aa2f96f6dec7a8814062bc50e4652bf9))


### Features

* add NetworkAudioSource component for synchronized audio playback ([1d9d8d5](https://github.com/PurrNet/PurrNet/commit/1d9d8d54b47e0b9a17fb91ae3e5e302b1bfcae75))
* add SetAnimator method to NetworkAnimator for improved animator management ([bcf69d2](https://github.com/PurrNet/PurrNet/commit/bcf69d28ef849af881e997f75e78477e63ba0bac))
* add Steam ID lookup for connections ([b3bf9ea](https://github.com/PurrNet/PurrNet/commit/b3bf9ea5e33325321e357785a876d5afbfdb7aac))
* add Unity services dependencies and update UTPClient/UTPServer for Relay support ([1523d06](https://github.com/PurrNet/PurrNet/commit/1523d0620d27a84ecf9db0ec3219ea4f9886a763))
* Network Rigidbody ([7ca21d4](https://github.com/PurrNet/PurrNet/commit/7ca21d43033c3ecc8b9e154f51782b5da14f5e54))

# [1.19.0-beta.23](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.22...v1.19.0-beta.23) (2026-02-20)


### Bug Fixes

* adjust MTU calculation to account for maximum bone entry size ([add7ebc](https://github.com/PurrNet/PurrNet/commit/add7ebcf7cab85b6b5aa8ad921620876ebd74114))

# [1.19.0-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.21...v1.19.0-beta.22) (2026-02-16)


### Bug Fixes

* Added addressables scenes support ([e35670b](https://github.com/PurrNet/PurrNet/commit/e35670b50a8fe08593daa088b315eee9f6767758))

# [1.19.0-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.20...v1.19.0-beta.21) (2026-02-15)


### Features

* add SetAnimator method to NetworkAnimator for improved animator management ([bcf69d2](https://github.com/PurrNet/PurrNet/commit/bcf69d28ef849af881e997f75e78477e63ba0bac))

# [1.19.0-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.19...v1.19.0-beta.20) (2026-02-13)


### Bug Fixes

* Improved Unity 5 support ([d228ee1](https://github.com/PurrNet/PurrNet/commit/d228ee1dc9ee91f73e743d03fba210a4eb32c723))

# [1.19.0-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.18...v1.19.0-beta.19) (2026-02-13)


### Bug Fixes

* ensure Awake is called before registering modules ([0b07224](https://github.com/PurrNet/PurrNet/commit/0b072244d039609817343b8a62ffd9b3ceca5785))

# [1.19.0-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.17...v1.19.0-beta.18) (2026-02-13)


### Bug Fixes

* Basic addressables spawning and despawning through network manager ([50a6c20](https://github.com/PurrNet/PurrNet/commit/50a6c204bfc6fcd159ef50166d29492ba453fd74))
* Composite Prefab Provider for NM ([c74f155](https://github.com/PurrNet/PurrNet/commit/c74f155c61c2a385d0c6258a722a4c4260d0cf96))
* notify user if modules can't be registered properly ([1f002e9](https://github.com/PurrNet/PurrNet/commit/1f002e9c50671a51605ab8f5d886053073c2485d))
* Push for version change ([2cb71d5](https://github.com/PurrNet/PurrNet/commit/2cb71d513db1f3540f38c9e9ffb913f3c3ff37ad))
* Small miss on the inspector ([0df0d5c](https://github.com/PurrNet/PurrNet/commit/0df0d5c22ac515243454f9a05d123b39047d3a95))
* Static observer RPC was failing due to bad Send function; removed Raw variants due to easy confusion point ([f7fc861](https://github.com/PurrNet/PurrNet/commit/f7fc8610e6c25c894cc6db5da7143f6fc74b79d4))
* update isControllingSyncVar logic to handle server updates correctly ([520e587](https://github.com/PurrNet/PurrNet/commit/520e587008e80341b6adde704e0af9dec87a172f))


### Features

* add NetworkAudioSource component for synchronized audio playback ([1d9d8d5](https://github.com/PurrNet/PurrNet/commit/1d9d8d54b47e0b9a17fb91ae3e5e302b1bfcae75))
* add Steam ID lookup for connections ([b3bf9ea](https://github.com/PurrNet/PurrNet/commit/b3bf9ea5e33325321e357785a876d5afbfdb7aac))

# [1.19.0-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.17...v1.19.0-beta.18) (2026-02-06)


### Bug Fixes

* update isControllingSyncVar logic to handle server updates correctly ([520e587](https://github.com/PurrNet/PurrNet/commit/520e587008e80341b6adde704e0af9dec87a172f))

# [1.19.0-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.16...v1.19.0-beta.17) (2026-02-06)


### Bug Fixes

* before failing to set syncvar double check if cached value is up to date; once client does a change to the syncvar, ingore server catchups ([f2cc835](https://github.com/PurrNet/PurrNet/commit/f2cc835a86300263197632ba5709938f4683b5b5))

# [1.19.0-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.15...v1.19.0-beta.16) (2026-02-05)


### Bug Fixes

* add null check for networkManager in PlayersManager retrieval ([567ac4f](https://github.com/PurrNet/PurrNet/commit/567ac4fb1e9022c774c843635db63d1c41c5fe86))

# [1.19.0-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.14...v1.19.0-beta.15) (2026-02-05)


### Bug Fixes

* BadImageFormatException: Method with open type while not compiling gshared ([2216218](https://github.com/PurrNet/PurrNet/commit/2216218a13f64507d2875b7691980251846ef8c4))

# [1.19.0-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.13...v1.19.0-beta.14) (2026-02-05)


### Bug Fixes

* scene module events not firing ([382094f](https://github.com/PurrNet/PurrNet/commit/382094f8ed40c82c556532dedfd133cd9c3bbd90))

# [1.19.0-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.12...v1.19.0-beta.13) (2026-02-03)


### Bug Fixes

* Backwards compatibility for network RB ([94af929](https://github.com/PurrNet/PurrNet/commit/94af9293ed711a98dfea2a83f35437ab285fc232))

# [1.19.0-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.11...v1.19.0-beta.12) (2026-02-03)


### Bug Fixes

* reworking bit packer functions ([0d6ca93](https://github.com/PurrNet/PurrNet/commit/0d6ca93638842268429fbccccb2ae397ec02ca12))

# [1.19.0-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.10...v1.19.0-beta.11) (2026-02-03)


### Bug Fixes

* add bounds checking to BitPacker.ReadBits to prevent native crash on malformed packets ([77abf0d](https://github.com/PurrNet/PurrNet/commit/77abf0dd8d177cf0fc0712aec7eda284c17e0af0))

# [1.19.0-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.9...v1.19.0-beta.10) (2026-02-03)


### Bug Fixes

* buffer overflow in RPC response handling ([6a83e07](https://github.com/PurrNet/PurrNet/commit/6a83e07e0d1ce1fbf525fd36ad57df6f3d38e455))

# [1.19.0-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.8...v1.19.0-beta.9) (2026-02-03)


### Features

* add Unity services dependencies and update UTPClient/UTPServer for Relay support ([1523d06](https://github.com/PurrNet/PurrNet/commit/1523d0620d27a84ecf9db0ec3219ea4f9886a763))

# [1.19.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.7...v1.19.0-beta.8) (2026-02-03)


### Bug Fixes

* compiler errors ([ae009e6](https://github.com/PurrNet/PurrNet/commit/ae009e64a10965bf78bdfd6a49d470337a94626f))

# [1.19.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.6...v1.19.0-beta.7) (2026-02-01)


### Bug Fixes

* allow to replace networkAssets at runtime ([d3dcd69](https://github.com/PurrNet/PurrNet/commit/d3dcd6924e1e87f43659542e20475d1e1cbba8fd))

# [1.19.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.5...v1.19.0-beta.6) (2026-01-30)


### Bug Fixes

* Added dynamic force correction to network RB ([c6a27cf](https://github.com/PurrNet/PurrNet/commit/c6a27cfbac65ec379c33c88bc8d939091a2b4ed7))
* Scaling hard correction threshold of Network RB ([39d5cb2](https://github.com/PurrNet/PurrNet/commit/39d5cb21b1ffba2c0db9e2210eabfe40b5fec525))
* Smooth rotational syncing on Network RB ([a4ca898](https://github.com/PurrNet/PurrNet/commit/a4ca898e5372365cf26f3c30ec968c1c6354891c))
* Solve inconsistency in sync timing ([59e7993](https://github.com/PurrNet/PurrNet/commit/59e7993fbdf64b5cee42e32b19593766e6d377bc))

# [1.19.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.4...v1.19.0-beta.5) (2026-01-29)


### Bug Fixes

* GetHashCode fails if list is null ([0ab9281](https://github.com/PurrNet/PurrNet/commit/0ab9281b48dce6cb9124dc7a6a601fbb7e7880eb))

# [1.19.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.3...v1.19.0-beta.4) (2026-01-28)


### Bug Fixes

* handle exceptions during tick processing in ServerTick method ([78f1408](https://github.com/PurrNet/PurrNet/commit/78f14086bede0a9411ec507e4f47edd191ad1ee3))

# [1.19.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.2...v1.19.0-beta.3) (2026-01-28)


### Bug Fixes

* make sure we account for local client being gone when handling ticks ([ca622e5](https://github.com/PurrNet/PurrNet/commit/ca622e51372b387b4f687d577341bfcd5f2223a5))

# [1.19.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.1...v1.19.0-beta.2) (2026-01-28)


### Bug Fixes

* InvokeLocal wasn't reseting position properly all the time ([66c8536](https://github.com/PurrNet/PurrNet/commit/66c8536ecde5bba6010104f7039cfab3e528a48c))

# [1.19.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.36...v1.19.0-beta.1) (2026-01-27)


### Features

* Network Rigidbody ([7ca21d4](https://github.com/PurrNet/PurrNet/commit/7ca21d43033c3ecc8b9e154f51782b5da14f5e54))

## [1.18.1-beta.36](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.35...v1.18.1-beta.36) (2026-01-27)


### Bug Fixes

* using after free ([890a29c](https://github.com/PurrNet/PurrNet/commit/890a29cd72bf96a70665da77c37aca34caaaa187))

## [1.18.1-beta.35](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.34...v1.18.1-beta.35) (2026-01-25)


### Bug Fixes

* target RPC to host's client failed, this was a regression ([6fdce17](https://github.com/PurrNet/PurrNet/commit/6fdce17673990deb758bd2d4b2348a00ad16c9dd))

## [1.18.1-beta.34](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.33...v1.18.1-beta.34) (2026-01-22)


### Bug Fixes

* initialize ping history size and stats on server connection ([5f8689a](https://github.com/PurrNet/PurrNet/commit/5f8689ae9ae785e2764725bb7f53a0c6ed018aa2))

## [1.18.1-beta.33](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.32...v1.18.1-beta.33) (2026-01-21)


### Bug Fixes

* DisposableArray duplicate ([af07f84](https://github.com/PurrNet/PurrNet/commit/af07f84b4d71d01a665aa797e71e58502ed11be9))

## [1.18.1-beta.32](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.31...v1.18.1-beta.32) (2026-01-21)


### Bug Fixes

* prevent destruction of objects in editor mode and cancel if it was already destroyed ([5351ba5](https://github.com/PurrNet/PurrNet/commit/5351ba52cd4d0569c9c1c05b349f83dd583a6351))

## [1.18.1-beta.31](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.30...v1.18.1-beta.31) (2026-01-20)


### Bug Fixes

* packer duplicate mistake; tests for hasher ([b66ca76](https://github.com/PurrNet/PurrNet/commit/b66ca76f528746e4aa126d90948482ec26e481e6))

## [1.18.1-beta.30](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.29...v1.18.1-beta.30) (2026-01-20)


### Bug Fixes

* add deterministic hash to the packer ([ceaaf4d](https://github.com/PurrNet/PurrNet/commit/ceaaf4d08db74443a67bdadf533bcd5d955d2c90))

## [1.18.1-beta.29](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.28...v1.18.1-beta.29) (2026-01-20)


### Bug Fixes

* hash collisions would break delta compression, added type explicitly to avoid this ([bc670ab](https://github.com/PurrNet/PurrNet/commit/bc670ab69682922c31c05fefae40c16f19f4f02f))

## [1.18.1-beta.28](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.27...v1.18.1-beta.28) (2026-01-20)


### Bug Fixes

* GetModule can fail here ([4ea9c5a](https://github.com/PurrNet/PurrNet/commit/4ea9c5ab6133cf14deace076f556649f78619cb6))

## [1.18.1-beta.27](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.26...v1.18.1-beta.27) (2026-01-19)


### Bug Fixes

* myers diff GC fixes and some tests for consistency ([6fc9525](https://github.com/PurrNet/PurrNet/commit/6fc95257d3aa4d0c2651b68e39e0a17d2bca8583))

## [1.18.1-beta.26](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.25...v1.18.1-beta.26) (2026-01-19)


### Bug Fixes

* dynamically changing region was error prone for the purrtransport ([92e2bb0](https://github.com/PurrNet/PurrNet/commit/92e2bb079091a474b5397327a7efe1e812e62235))

## [1.18.1-beta.25](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.24...v1.18.1-beta.25) (2026-01-18)


### Bug Fixes

* Quaternion equality check is stupid ([69b1c32](https://github.com/PurrNet/PurrNet/commit/69b1c32d766429307ce0d2b277c45994c53fd838))

## [1.18.1-beta.24](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.23...v1.18.1-beta.24) (2026-01-15)


### Bug Fixes

* remove commented-out code in PlayModePatch ([d1f866c](https://github.com/PurrNet/PurrNet/commit/d1f866c2a0c640764578a12c99b0a6241741492e))

## [1.18.1-beta.23](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.22...v1.18.1-beta.23) (2026-01-15)


### Bug Fixes

* improve network assets reliability and synchronization ([c508cd1](https://github.com/PurrNet/PurrNet/commit/c508cd10b733aa9ae841ad7b9a54712766b1a8fe))

## [1.18.1-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.21...v1.18.1-beta.22) (2026-01-14)


### Bug Fixes

* array comparison ([a278892](https://github.com/PurrNet/PurrNet/commit/a278892d44e3ca79d30a9d5becdc5469f07cff52))

## [1.18.1-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.20...v1.18.1-beta.21) (2026-01-14)


### Bug Fixes

* pass asServer parameter to TickManager in NetworkManager and RawNetManager ([0c288b3](https://github.com/PurrNet/PurrNet/commit/0c288b332028caf1f937014201f78ec9f5e8d688))

## [1.18.1-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.19...v1.18.1-beta.20) (2026-01-12)


### Bug Fixes

* add aggressive inlining to Duplicate method for JIT performance improvement ([f46516e](https://github.com/PurrNet/PurrNet/commit/f46516e8aafcd64759874fd3546148bb5cc06ce4))

## [1.18.1-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.18...v1.18.1-beta.19) (2026-01-12)


### Bug Fixes

* handle null selfRef and improve error handling in IEquatable generation ([44166b5](https://github.com/PurrNet/PurrNet/commit/44166b5a6d826afadcbb85c7d7f3af80586c1e54))

## [1.18.1-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.17...v1.18.1-beta.18) (2026-01-10)


### Bug Fixes

* delta module proper MTU usage instead of hard coded value ([6e9a1ef](https://github.com/PurrNet/PurrNet/commit/6e9a1efd41878cc010930a1ca1ed4be5e2118c6f))

## [1.18.1-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.16...v1.18.1-beta.17) (2026-01-09)


### Bug Fixes

* dont include rpc related things here ([f986586](https://github.com/PurrNet/PurrNet/commit/f98658604cdcc4266b52f090ce97c57e50d0ff12))

## [1.18.1-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.15...v1.18.1-beta.16) (2026-01-09)


### Bug Fixes

* if transform from A to B but types mismatch create it from scratch ([2e1799c](https://github.com/PurrNet/PurrNet/commit/2e1799c7bb5a3e4f78459093207f60601db21fa1))

## [1.18.1-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.14...v1.18.1-beta.15) (2026-01-09)


### Bug Fixes

* still cull rpcs when player isnt observer for other channels ([69c2677](https://github.com/PurrNet/PurrNet/commit/69c26771081843302480415c118fe556351b8cf7))

## [1.18.1-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.13...v1.18.1-beta.14) (2026-01-09)


### Bug Fixes

* Improved GC of statistics manager ([1d563a8](https://github.com/PurrNet/PurrNet/commit/1d563a8234393851ac2bdfdac9df6a80250a821a))

## [1.18.1-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.12...v1.18.1-beta.13) (2026-01-08)


### Bug Fixes

* Host migration should be enabled to check if it must force CanSee or not ([5814337](https://github.com/PurrNet/PurrNet/commit/58143374834ae94d908a8d552a6150d45bd45c00))

## [1.18.1-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.11...v1.18.1-beta.12) (2026-01-07)


### Bug Fixes

* properly call despawn() even if not fully spawned ([0b1739f](https://github.com/PurrNet/PurrNet/commit/0b1739feb6aba7e3eccef27d410898c4a435aeaf))

## [1.18.1-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.10...v1.18.1-beta.11) (2026-01-07)


### Bug Fixes

* Host migration should be enabled to check if it must force scene public or not. ([576e263](https://github.com/PurrNet/PurrNet/commit/576e263412f790af8c161fe1993d2fddbac39ebc))

## [1.18.1-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.9...v1.18.1-beta.10) (2026-01-03)


### Bug Fixes

* Whoopsie ([d2ec215](https://github.com/PurrNet/PurrNet/commit/d2ec2152b1da82740e6d7042b197f14f800cd151))

## [1.18.1-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.8...v1.18.1-beta.9) (2026-01-02)


### Bug Fixes

* expose latest read data properties for network transform ([65e6a23](https://github.com/PurrNet/PurrNet/commit/65e6a236b5819fec9e74002c886b245014713bbe))

## [1.18.1-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.7...v1.18.1-beta.8) (2026-01-02)


### Bug Fixes

* Sync dictionary serialization upgrade ([5934acb](https://github.com/PurrNet/PurrNet/commit/5934acbf9a52511353941040a1643728d658a75f))

## [1.18.1-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.6...v1.18.1-beta.7) (2026-01-02)


### Bug Fixes

* Added state machine current state helpers ([6ebfff9](https://github.com/PurrNet/PurrNet/commit/6ebfff9a934aadbf3e0c3010fff2632751e3538d))

## [1.18.1-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.5...v1.18.1-beta.6) (2025-12-31)


### Bug Fixes

* cleanup server/client modules, this caused some issues when next playthrough starting mode was changed ([65b2d0c](https://github.com/PurrNet/PurrNet/commit/65b2d0c6a330514edfb40d28c37cb75716b9a00d))
* syncvar invalidate is controller earlier ([2444436](https://github.com/PurrNet/PurrNet/commit/2444436aded533525be1e33930dab28a367e8cc2))
* wrong local variable index ([612cbfd](https://github.com/PurrNet/PurrNet/commit/612cbfd0aa2f96f6dec7a8814062bc50e4652bf9))

## [1.18.1-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.4...v1.18.1-beta.5) (2025-12-26)


### Bug Fixes

* ActualGetRelayServersAsync sometimes would come with an empty string and throw and exception ([c186213](https://github.com/PurrNet/PurrNet/commit/c1862139fb17591e678acfc40a9ded9886b5ee83))

## [1.18.1-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.3...v1.18.1-beta.4) (2025-12-22)


### Bug Fixes

* instead of throwing an exception lets just log what happened ([4a1c0b8](https://github.com/PurrNet/PurrNet/commit/4a1c0b8047d9ca08016a6f028a35cdf639a16483))
* some safety when cleaning client state ([f7011af](https://github.com/PurrNet/PurrNet/commit/f7011afaba24e90fdf80eb90a0509a274c68d1f4))

## [1.18.1-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.2...v1.18.1-beta.3) (2025-12-19)


### Bug Fixes

* fuck you c# ([d1986df](https://github.com/PurrNet/PurrNet/commit/d1986df12b414c59e1f81861677a7dfffa90b996))

## [1.18.1-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.1...v1.18.1-beta.2) (2025-12-19)


### Bug Fixes

* dont use static constructor ([0ad1ced](https://github.com/PurrNet/PurrNet/commit/0ad1ced6c34502bcfe91c298b7fe08fb250422ee))

## [1.18.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.18.0...v1.18.1-beta.1) (2025-12-19)


### Bug Fixes

* give each thread it's own bit packet pool ([0d5dd6c](https://github.com/PurrNet/PurrNet/commit/0d5dd6c40209cccf593cb880592a2c75a7ae4fb5))

# [1.18.0](https://github.com/PurrNet/PurrNet/compare/v1.17.0...v1.18.0) (2025-12-18)


### Bug Fixes

* Added sync event without data ([4674dfe](https://github.com/PurrNet/PurrNet/commit/4674dfea96fef1cfbf4b9e6618c76564415af52e))
* allow PurrTransport.cs to kick connections ([f1fcc4a](https://github.com/PurrNet/PurrNet/commit/f1fcc4a72c34549dcb86a78f0567124563c4a433))
* allow to override `propagateToChildren` when giving/removing ownership ([8fb5cf7](https://github.com/PurrNet/PurrNet/commit/8fb5cf73b2571e46e022bfd046f72a711f50bfb2))
* Big cleanup of SyncEvent ([633a0ce](https://github.com/PurrNet/PurrNet/commit/633a0cebb936f4398d1d7b37f5802103dae9388b))
* big data bugs and sync texture! ([ca70b79](https://github.com/PurrNet/PurrNet/commit/ca70b79dc55cb42937dcdc2f041c3c9a1ec0e5c2))
* clear connections when SteamTransport starts ([c77f166](https://github.com/PurrNet/PurrNet/commit/c77f16671bc0f488e10df8fca4db1bb96d438a6f))
* clear partial data if owner disconnected mid download ([0bb0cd9](https://github.com/PurrNet/PurrNet/commit/0bb0cd973e0badcaa4ccea88320b9bc44f477408))
* despawn event not called, by the time OnDestroy arrives the owner info is wrong ([d0c090e](https://github.com/PurrNet/PurrNet/commit/d0c090ef72980430fe8c237877d07645a4f3261f))
* don't auto despawn manually spawned identities ([ca52e95](https://github.com/PurrNet/PurrNet/commit/ca52e951c8de5d59c3684392f0e4e83eb5f6fac5))
* double download/update bug for big data ([a785e7f](https://github.com/PurrNet/PurrNet/commit/a785e7f7c5652f0452b552964f0bb614b9a7051f))
* for 6.3+ doesnt make sense to allow for None so lets just ignore it ([7c4bfac](https://github.com/PurrNet/PurrNet/commit/7c4bfacf2313367752120059c3ded04d103f53e2))
* generic RPC bad formated IL ([c090c17](https://github.com/PurrNet/PurrNet/commit/c090c178901f2e8f673d9518f0781a7f6de796c8))
* half quaternion acting up ([9bf5a2c](https://github.com/PurrNet/PurrNet/commit/9bf5a2cdf919eabc8fd64e0ef0906b92d262b19e))
* if server is starting wait for it to fully spin up before starting client ([4bbf0d6](https://github.com/PurrNet/PurrNet/commit/4bbf0d6bc233d7029dec93607991a3ec3850712d))
* make sure big data doesn't mix with old big data (unreliable) ([cabc112](https://github.com/PurrNet/PurrNet/commit/cabc1126c88f47b11659c309448efe6441bd5ef9))
* merging of additions was too aggressive ([77549fe](https://github.com/PurrNet/PurrNet/commit/77549fe8a30873b638a717110dd7d5a6a4ef81ac))
* MTU overflow bug ([2036d21](https://github.com/PurrNet/PurrNet/commit/2036d21b4bfd3c67ece85ba38d958c5508d72d7e))
* network rule to allow target rpcs to target server ([e8339c3](https://github.com/PurrNet/PurrNet/commit/e8339c3b8b301ff4a2520fbb00554afc9752fc85))
* NetworkBones.cs cached ID was not being reset when packet was split ([96dcb34](https://github.com/PurrNet/PurrNet/commit/96dcb34613ea279b65d1a6d9f098aca8c62d9460))
* observer events need to flush RPCs for the onspawned to be processed correctly ([67cf99f](https://github.com/PurrNet/PurrNet/commit/67cf99f2da195a3da29a9d33698b95d85f3361e1))
* obsolete code in unity 6.3+ ([0c404b6](https://github.com/PurrNet/PurrNet/commit/0c404b6e40e736890fde757ce1bbad79048d2a87))
* packer crashing issue due to bad argument handling for method invocation ([a376d3c](https://github.com/PurrNet/PurrNet/commit/a376d3cf5600ea0d1717c42c6b0eb84c3fc7bae5))
* parenting was broken from previous NT rework ([9536c5d](https://github.com/PurrNet/PurrNet/commit/9536c5d180b7e7f22ccce5c7b452128c091434c0))
* Player Identity AOT safety ([520b268](https://github.com/PurrNet/PurrNet/commit/520b2682d214ff97e1a5b922f6fe40ad13ef3a9a))
* pooled array not being cleared broke some functions ([70b8121](https://github.com/PurrNet/PurrNet/commit/70b81214b858a2ac3d4917c1fa3d7eadd9a548f2))
* remove obsolete code ([20c381a](https://github.com/PurrNet/PurrNet/commit/20c381a564b35c93507147a6b42f0ff325341ae1))
* rpc batching and ownership ([bd35c80](https://github.com/PurrNet/PurrNet/commit/bd35c80434a2c1de288fd1a75b504f77ae4e010d))
* send parent change on event instead of delaying it further ([7e03bd6](https://github.com/PurrNet/PurrNet/commit/7e03bd6b03137d38533eb52a646efd0dbbcbb870))
* simplify float delta packing, old packer just added more overhead ([3d2b0f0](https://github.com/PurrNet/PurrNet/commit/3d2b0f0e773621ac2744190a0a898511f722679d))
* some network transform ordering issues ([cb4eed1](https://github.com/PurrNet/PurrNet/commit/cb4eed1355797d0990244447aacb221eba8f20aa))
* spawning concurrency bug ([6af756e](https://github.com/PurrNet/PurrNet/commit/6af756ecaead37b8e73a40326dd591fe055af0c7))
* SyncBigData.cs now supports owner auth and switching ([80a6932](https://github.com/PurrNet/PurrNet/commit/80a693297d4ea51d38f2633a6d4fc1b408d0c266))
* SyncList owner auth fix ([19d044f](https://github.com/PurrNet/PurrNet/commit/19d044f76d1af20cf91da1dcb92cb5ee63e8920d))
* SyncTimer fix from Valentins mistake ([afd5d42](https://github.com/PurrNet/PurrNet/commit/afd5d42ab9897e1fcb87e646e6d8057d198ea461))
* syncvar cleanup and optimisations ([6eae47b](https://github.com/PurrNet/PurrNet/commit/6eae47b1d5b5e25d5b667ec17af77e1a2f05f3d9))
* udp disconnect reason ([0156338](https://github.com/PurrNet/PurrNet/commit/01563389b31084fbd69370e784839cbc92b61fa2))
* unity 6.3 toolbar fixes ([7619b8e](https://github.com/PurrNet/PurrNet/commit/7619b8e462d860258c733d3d83b008dee033369b))
* use HierarchyV2.SetLocalPosAndRot instead of dup logic ([3732cbd](https://github.com/PurrNet/PurrNet/commit/3732cbdeef22c4d295233cee973bbb498584834c))
* when playmode window layout changes it clears the wrapped GUI... ([49b5554](https://github.com/PurrNet/PurrNet/commit/49b555430b7c06c9b026c54f2df617ef3db0e665))


### Features

* add a new rule 'enable host migration' ([7b9b083](https://github.com/PurrNet/PurrNet/commit/7b9b083a94773370ba2cd004a07e349eb2c4d8cd))
* promoting client to server ([36569e4](https://github.com/PurrNet/PurrNet/commit/36569e494030125dccf757572e2debc54e470ca3))
* rpc batching and header delta compression ([639532c](https://github.com/PurrNet/PurrNet/commit/639532c4a790e1b8970c67737a79d54c53f69e58))

# [1.18.0-beta.27](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.26...v1.18.0-beta.27) (2025-12-16)


### Bug Fixes

* packer crashing issue due to bad argument handling for method invocation ([a376d3c](https://github.com/PurrNet/PurrNet/commit/a376d3cf5600ea0d1717c42c6b0eb84c3fc7bae5))

# [1.18.0-beta.26](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.25...v1.18.0-beta.26) (2025-12-16)


### Bug Fixes

* send parent change on event instead of delaying it further ([7e03bd6](https://github.com/PurrNet/PurrNet/commit/7e03bd6b03137d38533eb52a646efd0dbbcbb870))

# [1.18.0-beta.25](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.24...v1.18.0-beta.25) (2025-12-15)


### Bug Fixes

* Added sync event without data ([4674dfe](https://github.com/PurrNet/PurrNet/commit/4674dfea96fef1cfbf4b9e6618c76564415af52e))

# [1.18.0-beta.24](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.23...v1.18.0-beta.24) (2025-12-15)


### Bug Fixes

* if server is starting wait for it to fully spin up before starting client ([4bbf0d6](https://github.com/PurrNet/PurrNet/commit/4bbf0d6bc233d7029dec93607991a3ec3850712d))

# [1.18.0-beta.23](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.22...v1.18.0-beta.23) (2025-12-15)


### Bug Fixes

* clear connections when SteamTransport starts ([c77f166](https://github.com/PurrNet/PurrNet/commit/c77f16671bc0f488e10df8fca4db1bb96d438a6f))

# [1.18.0-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.21...v1.18.0-beta.22) (2025-12-14)


### Bug Fixes

* simplify float delta packing, old packer just added more overhead ([3d2b0f0](https://github.com/PurrNet/PurrNet/commit/3d2b0f0e773621ac2744190a0a898511f722679d))

# [1.18.0-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.20...v1.18.0-beta.21) (2025-12-14)


### Bug Fixes

* NetworkBones.cs cached ID was not being reset when packet was split ([96dcb34](https://github.com/PurrNet/PurrNet/commit/96dcb34613ea279b65d1a6d9f098aca8c62d9460))

# [1.18.0-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.19...v1.18.0-beta.20) (2025-12-13)


### Bug Fixes

* SyncTimer fix from Valentins mistake ([afd5d42](https://github.com/PurrNet/PurrNet/commit/afd5d42ab9897e1fcb87e646e6d8057d198ea461))

# [1.18.0-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.18...v1.18.0-beta.19) (2025-12-12)


### Bug Fixes

* allow to override `propagateToChildren` when giving/removing ownership ([8fb5cf7](https://github.com/PurrNet/PurrNet/commit/8fb5cf73b2571e46e022bfd046f72a711f50bfb2))

# [1.18.0-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.17...v1.18.0-beta.18) (2025-12-11)


### Bug Fixes

* MTU overflow bug ([2036d21](https://github.com/PurrNet/PurrNet/commit/2036d21b4bfd3c67ece85ba38d958c5508d72d7e))

# [1.18.0-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.16...v1.18.0-beta.17) (2025-12-09)


### Bug Fixes

* don't auto despawn manually spawned identities ([ca52e95](https://github.com/PurrNet/PurrNet/commit/ca52e951c8de5d59c3684392f0e4e83eb5f6fac5))
* for 6.3+ doesnt make sense to allow for None so lets just ignore it ([7c4bfac](https://github.com/PurrNet/PurrNet/commit/7c4bfacf2313367752120059c3ded04d103f53e2))
* obsolete code in unity 6.3+ ([0c404b6](https://github.com/PurrNet/PurrNet/commit/0c404b6e40e736890fde757ce1bbad79048d2a87))
* udp disconnect reason ([0156338](https://github.com/PurrNet/PurrNet/commit/01563389b31084fbd69370e784839cbc92b61fa2))
* unity 6.3 toolbar fixes ([7619b8e](https://github.com/PurrNet/PurrNet/commit/7619b8e462d860258c733d3d83b008dee033369b))
* when playmode window layout changes it clears the wrapped GUI... ([49b5554](https://github.com/PurrNet/PurrNet/commit/49b555430b7c06c9b026c54f2df617ef3db0e665))


### Features

* add a new rule 'enable host migration' ([7b9b083](https://github.com/PurrNet/PurrNet/commit/7b9b083a94773370ba2cd004a07e349eb2c4d8cd))
* promoting client to server ([36569e4](https://github.com/PurrNet/PurrNet/commit/36569e494030125dccf757572e2debc54e470ca3))

# [1.18.0-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.15...v1.18.0-beta.16) (2025-12-09)


### Bug Fixes

* Big cleanup of SyncEvent ([633a0ce](https://github.com/PurrNet/PurrNet/commit/633a0cebb936f4398d1d7b37f5802103dae9388b))

# [1.18.0-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.14...v1.18.0-beta.15) (2025-12-07)


### Bug Fixes

* allow PurrTransport.cs to kick connections ([f1fcc4a](https://github.com/PurrNet/PurrNet/commit/f1fcc4a72c34549dcb86a78f0567124563c4a433))

# [1.18.0-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.13...v1.18.0-beta.14) (2025-12-05)


### Bug Fixes

* despawn event not called, by the time OnDestroy arrives the owner info is wrong ([d0c090e](https://github.com/PurrNet/PurrNet/commit/d0c090ef72980430fe8c237877d07645a4f3261f))

# [1.18.0-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.12...v1.18.0-beta.13) (2025-12-05)


### Bug Fixes

* observer events need to flush RPCs for the onspawned to be processed correctly ([67cf99f](https://github.com/PurrNet/PurrNet/commit/67cf99f2da195a3da29a9d33698b95d85f3361e1))

# [1.18.0-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.11...v1.18.0-beta.12) (2025-12-04)


### Bug Fixes

* use HierarchyV2.SetLocalPosAndRot instead of dup logic ([3732cbd](https://github.com/PurrNet/PurrNet/commit/3732cbdeef22c4d295233cee973bbb498584834c))

# [1.18.0-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.10...v1.18.0-beta.11) (2025-12-04)


### Bug Fixes

* Player Identity AOT safety ([520b268](https://github.com/PurrNet/PurrNet/commit/520b2682d214ff97e1a5b922f6fe40ad13ef3a9a))

# [1.18.0-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.9...v1.18.0-beta.10) (2025-12-03)


### Bug Fixes

* generic RPC bad formated IL ([c090c17](https://github.com/PurrNet/PurrNet/commit/c090c178901f2e8f673d9518f0781a7f6de796c8))

# [1.18.0-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.8...v1.18.0-beta.9) (2025-12-02)


### Bug Fixes

* syncvar cleanup and optimisations ([6eae47b](https://github.com/PurrNet/PurrNet/commit/6eae47b1d5b5e25d5b667ec17af77e1a2f05f3d9))

# [1.18.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.7...v1.18.0-beta.8) (2025-11-28)


### Bug Fixes

* SyncList owner auth fix ([19d044f](https://github.com/PurrNet/PurrNet/commit/19d044f76d1af20cf91da1dcb92cb5ee63e8920d))

# [1.18.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.6...v1.18.0-beta.7) (2025-11-27)


### Bug Fixes

* clear partial data if owner disconnected mid download ([0bb0cd9](https://github.com/PurrNet/PurrNet/commit/0bb0cd973e0badcaa4ccea88320b9bc44f477408))

# [1.18.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.5...v1.18.0-beta.6) (2025-11-24)


### Bug Fixes

* merging of additions was too aggressive ([77549fe](https://github.com/PurrNet/PurrNet/commit/77549fe8a30873b638a717110dd7d5a6a4ef81ac))

# [1.18.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.4...v1.18.0-beta.5) (2025-11-24)


### Bug Fixes

* half quaternion acting up ([9bf5a2c](https://github.com/PurrNet/PurrNet/commit/9bf5a2cdf919eabc8fd64e0ef0906b92d262b19e))

# [1.18.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.3...v1.18.0-beta.4) (2025-11-24)


### Bug Fixes

* rpc batching and ownership ([bd35c80](https://github.com/PurrNet/PurrNet/commit/bd35c80434a2c1de288fd1a75b504f77ae4e010d))

# [1.18.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.2...v1.18.0-beta.3) (2025-11-23)


### Bug Fixes

* some network transform ordering issues ([cb4eed1](https://github.com/PurrNet/PurrNet/commit/cb4eed1355797d0990244447aacb221eba8f20aa))

# [1.18.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.1...v1.18.0-beta.2) (2025-11-23)


### Bug Fixes

* pooled array not being cleared broke some functions ([70b8121](https://github.com/PurrNet/PurrNet/commit/70b81214b858a2ac3d4917c1fa3d7eadd9a548f2))

# [1.18.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.17.1-beta.8...v1.18.0-beta.1) (2025-11-21)


### Features

* rpc batching and header delta compression ([639532c](https://github.com/PurrNet/PurrNet/commit/639532c4a790e1b8970c67737a79d54c53f69e58))

## [1.17.1-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.17.1-beta.7...v1.17.1-beta.8) (2025-11-20)


### Bug Fixes

* double download/update bug for big data ([a785e7f](https://github.com/PurrNet/PurrNet/commit/a785e7f7c5652f0452b552964f0bb614b9a7051f))

## [1.17.1-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.17.1-beta.6...v1.17.1-beta.7) (2025-11-19)


### Bug Fixes

* big data bugs and sync texture! ([ca70b79](https://github.com/PurrNet/PurrNet/commit/ca70b79dc55cb42937dcdc2f041c3c9a1ec0e5c2))

## [1.17.1-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.17.1-beta.5...v1.17.1-beta.6) (2025-11-19)


### Bug Fixes

* remove obsolete code ([20c381a](https://github.com/PurrNet/PurrNet/commit/20c381a564b35c93507147a6b42f0ff325341ae1))

## [1.17.1-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.17.1-beta.4...v1.17.1-beta.5) (2025-11-19)


### Bug Fixes

* spawning concurrency bug ([6af756e](https://github.com/PurrNet/PurrNet/commit/6af756ecaead37b8e73a40326dd591fe055af0c7))

## [1.17.1-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.17.1-beta.3...v1.17.1-beta.4) (2025-11-19)


### Bug Fixes

* parenting was broken from previous NT rework ([9536c5d](https://github.com/PurrNet/PurrNet/commit/9536c5d180b7e7f22ccce5c7b452128c091434c0))

## [1.17.1-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.17.1-beta.2...v1.17.1-beta.3) (2025-11-19)


### Bug Fixes

* make sure big data doesn't mix with old big data (unreliable) ([cabc112](https://github.com/PurrNet/PurrNet/commit/cabc1126c88f47b11659c309448efe6441bd5ef9))

## [1.17.1-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.17.1-beta.1...v1.17.1-beta.2) (2025-11-19)


### Bug Fixes

* SyncBigData.cs now supports owner auth and switching ([80a6932](https://github.com/PurrNet/PurrNet/commit/80a693297d4ea51d38f2633a6d4fc1b408d0c266))

## [1.17.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.17.0...v1.17.1-beta.1) (2025-11-19)


### Bug Fixes

* network rule to allow target rpcs to target server ([e8339c3](https://github.com/PurrNet/PurrNet/commit/e8339c3b8b301ff4a2520fbb00554afc9752fc85))

# [1.17.0](https://github.com/PurrNet/PurrNet/compare/v1.16.0...v1.17.0) (2025-11-18)


### Bug Fixes

* actually register disposableArray ([c0f7b0f](https://github.com/PurrNet/PurrNet/commit/c0f7b0f85213304459111acbaa607dd834dbf7e1))
* Added action subscription to syncevent ([e30ed2a](https://github.com/PurrNet/PurrNet/commit/e30ed2a40f406fe833f5453cbdba4bfdf4fa32ec))
* Added summary for new sync timer advancing ([0b5be52](https://github.com/PurrNet/PurrNet/commit/0b5be5285c2458e1351da220a6c1088699514d2f))
* allow to manually clear all subscriptions from network manaher using `ResetInternalState` ([f24f0a8](https://github.com/PurrNet/PurrNet/commit/f24f0a897d64b19a7d7108f6634b87ed2d844e3e))
* allow to override how things are duplicated ([c99ba8f](https://github.com/PurrNet/PurrNet/commit/c99ba8f122f6b10bf1bf7f335d1b1df623ec7781))
* bad symbol name ([0d94952](https://github.com/PurrNet/PurrNet/commit/0d949526be21c408ab0a979965fffde58b2a2ac2))
* building errors ([d0ce345](https://github.com/PurrNet/PurrNet/commit/d0ce34535f668d9bad6759385d3bf3c455d12c83))
* clear static stuff for PlayerIdentity.cs ([da22af9](https://github.com/PurrNet/PurrNet/commit/da22af9c0d1b433e700130484622259dd763ca33))
* compiler error due to bad #if ([41cc6c6](https://github.com/PurrNet/PurrNet/commit/41cc6c62928d1f9dd037751e9f44b729b82399c8))
* error when adding component at runtime ([a7d068c](https://github.com/PurrNet/PurrNet/commit/a7d068c81001e645d49921f9caced42c7eef7840))
* forwarding data with delta packer ([135e8df](https://github.com/PurrNet/PurrNet/commit/135e8df2f9147fd2100d1edc71c87d13bb45c265))
* IDuplicates of the disposable collections were wrong for managed types ([a11a9be](https://github.com/PurrNet/PurrNet/commit/a11a9be2ca2a249328a21b4cc0376fcd501a7d5e))
* if collection is disposed just return default value ([80cc7a9](https://github.com/PurrNet/PurrNet/commit/80cc7a9e0434d482ee6630641eb00c6a9445db2e))
* Improved validated syncvar handling ([4fe4434](https://github.com/PurrNet/PurrNet/commit/4fe443447d15264ff6f110fa4e12978b347ad020))
* make ownership clearer for InterlatedWithDispose ([2615cc7](https://github.com/PurrNet/PurrNet/commit/2615cc7944bbed4d4c13ec25f2e77cc480ccc3bb))
* make sure `OnTick` exceptions don't have side effects to other subscribers ([8811429](https://github.com/PurrNet/PurrNet/commit/88114295949b5b64936c0becb7d5854f2714a744))
* order of add was screwed by my previous attempt to be smart ([315dd96](https://github.com/PurrNet/PurrNet/commit/315dd966ef007c8e08e355aeefb58f6e77415ba5))
* PlayerIdentity<T> catchup in OnSpawned when it happens at a later stage ([53305a4](https://github.com/PurrNet/PurrNet/commit/53305a468694272e151bb1bbc7e6901cc8bb3f02))
* previous undo compiler errors ([a889ac8](https://github.com/PurrNet/PurrNet/commit/a889ac894ce77a344491395b154c604e54beb1ef))
* profiler/statistics locked to editor only ([d8272f7](https://github.com/PurrNet/PurrNet/commit/d8272f747871778d69a68de5ea7943dc93769121))
* properly filter players when forwarding rpcs ([3b05719](https://github.com/PurrNet/PurrNet/commit/3b057195282236c496337875544d938946ce4731))
* properly register interfaces and collections of interfaces ([dd0b28e](https://github.com/PurrNet/PurrNet/commit/dd0b28edb6f5c0ea06415eba4c9ee1ed577764a5))
* PURR_LEAKS_CHECK for dictionaries too ([3c1b8d6](https://github.com/PurrNet/PurrNet/commit/3c1b8d652685969fe82f5a2341a6073d75ac3414))
* scene pooling not clearing properly ([1acdd22](https://github.com/PurrNet/PurrNet/commit/1acdd222acf5fc86b1ac3ef7b01d507f3b9137e7))
* State machine host fix ([15bfbcd](https://github.com/PurrNet/PurrNet/commit/15bfbcd1a912ad2a9bceb50dbbb3d8468005595d))
* static delta rpcs ([77ce2b4](https://github.com/PurrNet/PurrNet/commit/77ce2b4815eb474ef4bb308799c0d3cfafe4209f))
* syncvar events should be triggered when Packer.Transform is successful ([abbc918](https://github.com/PurrNet/PurrNet/commit/abbc918f033be615831efdcc2aa61835707c71d7))
* try to match static rpc behaviour to normal rpcs ([267f5c5](https://github.com/PurrNet/PurrNet/commit/267f5c57e0d5d9efe6908a0d6ec1284c565d84d2))
* trying to make DDOL deterministic ([d03647c](https://github.com/PurrNet/PurrNet/commit/d03647c71602ef588c165bf86854be9bf7f5efad))
* useDeltaPacking for rpcs, still WIP ([1611074](https://github.com/PurrNet/PurrNet/commit/16110740d3f9ae7829b70b90880ebc2aa4947b91))
* work around c# methods that generate GC ([59d660f](https://github.com/PurrNet/PurrNet/commit/59d660fcda78f1fe5543350ad28c6035accba61c))


### Features

* Added Validated Syncvar ([49f9343](https://github.com/PurrNet/PurrNet/commit/49f9343b86e10e9f8d1b1db5c5f4c878aa19eb2f))

# [1.17.0-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.21...v1.17.0-beta.22) (2025-11-13)


### Bug Fixes

* Added action subscription to syncevent ([e30ed2a](https://github.com/PurrNet/PurrNet/commit/e30ed2a40f406fe833f5453cbdba4bfdf4fa32ec))

# [1.17.0-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.20...v1.17.0-beta.21) (2025-11-13)


### Bug Fixes

* order of add was screwed by my previous attempt to be smart ([315dd96](https://github.com/PurrNet/PurrNet/commit/315dd966ef007c8e08e355aeefb58f6e77415ba5))

# [1.17.0-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.19...v1.17.0-beta.20) (2025-11-13)


### Bug Fixes

* building errors ([d0ce345](https://github.com/PurrNet/PurrNet/commit/d0ce34535f668d9bad6759385d3bf3c455d12c83))

# [1.17.0-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.18...v1.17.0-beta.19) (2025-11-13)


### Bug Fixes

* if collection is disposed just return default value ([80cc7a9](https://github.com/PurrNet/PurrNet/commit/80cc7a9e0434d482ee6630641eb00c6a9445db2e))

# [1.17.0-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.17...v1.17.0-beta.18) (2025-11-13)


### Bug Fixes

* work around c# methods that generate GC ([59d660f](https://github.com/PurrNet/PurrNet/commit/59d660fcda78f1fe5543350ad28c6035accba61c))

# [1.17.0-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.16...v1.17.0-beta.17) (2025-11-13)


### Bug Fixes

* IDuplicates of the disposable collections were wrong for managed types ([a11a9be](https://github.com/PurrNet/PurrNet/commit/a11a9be2ca2a249328a21b4cc0376fcd501a7d5e))

# [1.17.0-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.15...v1.17.0-beta.16) (2025-11-13)


### Bug Fixes

* make ownership clearer for InterlatedWithDispose ([2615cc7](https://github.com/PurrNet/PurrNet/commit/2615cc7944bbed4d4c13ec25f2e77cc480ccc3bb))

# [1.17.0-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.14...v1.17.0-beta.15) (2025-11-12)


### Bug Fixes

* make sure `OnTick` exceptions don't have side effects to other subscribers ([8811429](https://github.com/PurrNet/PurrNet/commit/88114295949b5b64936c0becb7d5854f2714a744))

# [1.17.0-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.13...v1.17.0-beta.14) (2025-11-12)


### Bug Fixes

* scene pooling not clearing properly ([1acdd22](https://github.com/PurrNet/PurrNet/commit/1acdd222acf5fc86b1ac3ef7b01d507f3b9137e7))

# [1.17.0-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.12...v1.17.0-beta.13) (2025-11-11)


### Bug Fixes

* trying to make DDOL deterministic ([d03647c](https://github.com/PurrNet/PurrNet/commit/d03647c71602ef588c165bf86854be9bf7f5efad))

# [1.17.0-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.11...v1.17.0-beta.12) (2025-11-11)


### Bug Fixes

* properly filter players when forwarding rpcs ([3b05719](https://github.com/PurrNet/PurrNet/commit/3b057195282236c496337875544d938946ce4731))

# [1.17.0-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.10...v1.17.0-beta.11) (2025-11-11)


### Bug Fixes

* static delta rpcs ([77ce2b4](https://github.com/PurrNet/PurrNet/commit/77ce2b4815eb474ef4bb308799c0d3cfafe4209f))
* syncvar events should be triggered when Packer.Transform is successful ([abbc918](https://github.com/PurrNet/PurrNet/commit/abbc918f033be615831efdcc2aa61835707c71d7))

# [1.17.0-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.9...v1.17.0-beta.10) (2025-11-11)


### Bug Fixes

* compiler error due to bad #if ([41cc6c6](https://github.com/PurrNet/PurrNet/commit/41cc6c62928d1f9dd037751e9f44b729b82399c8))

# [1.17.0-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.8...v1.17.0-beta.9) (2025-11-10)


### Bug Fixes

* allow to manually clear all subscriptions from network manaher using `ResetInternalState` ([f24f0a8](https://github.com/PurrNet/PurrNet/commit/f24f0a897d64b19a7d7108f6634b87ed2d844e3e))

# [1.17.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.7...v1.17.0-beta.8) (2025-11-10)


### Bug Fixes

* previous undo compiler errors ([a889ac8](https://github.com/PurrNet/PurrNet/commit/a889ac894ce77a344491395b154c604e54beb1ef))

# [1.17.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.6...v1.17.0-beta.7) (2025-11-07)


### Bug Fixes

* forwarding data with delta packer ([135e8df](https://github.com/PurrNet/PurrNet/commit/135e8df2f9147fd2100d1edc71c87d13bb45c265))

# [1.17.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.5...v1.17.0-beta.6) (2025-11-06)


### Bug Fixes

* useDeltaPacking for rpcs, still WIP ([1611074](https://github.com/PurrNet/PurrNet/commit/16110740d3f9ae7829b70b90880ebc2aa4947b91))

# [1.17.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.4...v1.17.0-beta.5) (2025-11-04)


### Bug Fixes

* clear static stuff for PlayerIdentity.cs ([da22af9](https://github.com/PurrNet/PurrNet/commit/da22af9c0d1b433e700130484622259dd763ca33))

# [1.17.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.3...v1.17.0-beta.4) (2025-11-04)


### Bug Fixes

* try to match static rpc behaviour to normal rpcs ([267f5c5](https://github.com/PurrNet/PurrNet/commit/267f5c57e0d5d9efe6908a0d6ec1284c565d84d2))

# [1.17.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.2...v1.17.0-beta.3) (2025-11-04)


### Bug Fixes

* PlayerIdentity<T> catchup in OnSpawned when it happens at a later stage ([53305a4](https://github.com/PurrNet/PurrNet/commit/53305a468694272e151bb1bbc7e6901cc8bb3f02))

# [1.17.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.1...v1.17.0-beta.2) (2025-11-03)


### Bug Fixes

* Improved validated syncvar handling ([4fe4434](https://github.com/PurrNet/PurrNet/commit/4fe443447d15264ff6f110fa4e12978b347ad020))

# [1.17.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.16.1-beta.8...v1.17.0-beta.1) (2025-11-03)


### Features

* Added Validated Syncvar ([49f9343](https://github.com/PurrNet/PurrNet/commit/49f9343b86e10e9f8d1b1db5c5f4c878aa19eb2f))

## [1.16.1-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.16.1-beta.7...v1.16.1-beta.8) (2025-11-03)


### Bug Fixes

* Added summary for new sync timer advancing ([0b5be52](https://github.com/PurrNet/PurrNet/commit/0b5be5285c2458e1351da220a6c1088699514d2f))

## [1.16.1-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.16.1-beta.6...v1.16.1-beta.7) (2025-11-02)


### Bug Fixes

* properly register interfaces and collections of interfaces ([dd0b28e](https://github.com/PurrNet/PurrNet/commit/dd0b28edb6f5c0ea06415eba4c9ee1ed577764a5))

## [1.16.1-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.16.1-beta.5...v1.16.1-beta.6) (2025-10-28)


### Bug Fixes

* allow to override how things are duplicated ([c99ba8f](https://github.com/PurrNet/PurrNet/commit/c99ba8f122f6b10bf1bf7f335d1b1df623ec7781))

## [1.16.1-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.16.1-beta.4...v1.16.1-beta.5) (2025-10-27)


### Bug Fixes

* PURR_LEAKS_CHECK for dictionaries too ([3c1b8d6](https://github.com/PurrNet/PurrNet/commit/3c1b8d652685969fe82f5a2341a6073d75ac3414))

## [1.16.1-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.16.1-beta.3...v1.16.1-beta.4) (2025-10-26)


### Bug Fixes

* error when adding component at runtime ([a7d068c](https://github.com/PurrNet/PurrNet/commit/a7d068c81001e645d49921f9caced42c7eef7840))

## [1.16.1-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.16.1-beta.2...v1.16.1-beta.3) (2025-10-24)


### Bug Fixes

* bad symbol name ([0d94952](https://github.com/PurrNet/PurrNet/commit/0d949526be21c408ab0a979965fffde58b2a2ac2))
* profiler/statistics locked to editor only ([d8272f7](https://github.com/PurrNet/PurrNet/commit/d8272f747871778d69a68de5ea7943dc93769121))

## [1.16.1-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.16.1-beta.1...v1.16.1-beta.2) (2025-10-23)


### Bug Fixes

* actually register disposableArray ([c0f7b0f](https://github.com/PurrNet/PurrNet/commit/c0f7b0f85213304459111acbaa607dd834dbf7e1))

## [1.16.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.16.0...v1.16.1-beta.1) (2025-10-22)


### Bug Fixes

* State machine host fix ([15bfbcd](https://github.com/PurrNet/PurrNet/commit/15bfbcd1a912ad2a9bceb50dbbb3d8468005595d))

# [1.16.0](https://github.com/PurrNet/PurrNet/compare/v1.15.0...v1.16.0) (2025-10-17)


### Bug Fixes

* +disposable array ([4c92504](https://github.com/PurrNet/PurrNet/commit/4c925042940abf14b839d1b5b6a7063a56be34b2))
* add `HierarchyV2.onPreSpawn` static event ([0c02749](https://github.com/PurrNet/PurrNet/commit/0c0274922d2cfb1db576c4bb4fbfa4d1e73f50f6))
* add debugging scripting symbol for delta compression that packs extra data to let you know of issues ([2c96204](https://github.com/PurrNet/PurrNet/commit/2c9620425c2ae43e545bbc04ed94205f02e75d6e))
* Added git checking to addon library ([336e590](https://github.com/PurrNet/PurrNet/commit/336e59027c71b3ed96a6dc6d8f4f5a699a6af213))
* Added PurrChat link to toolbar ([72d3d58](https://github.com/PurrNet/PurrNet/commit/72d3d58cf6be44a925837f736c49b7a1af92e93b))
* Added statistics manager display target options ([75d095f](https://github.com/PurrNet/PurrNet/commit/75d095ff1881ed47d836c9af63baf21c90bc09e9))
* allow network animator to reconcile time ([caa62bc](https://github.com/PurrNet/PurrNet/commit/caa62bc81078a38cf5381dad5f888e6186ee3089))
* allow to prioritize non generated packers more explicitly ([0081fbc](https://github.com/PurrNet/PurrNet/commit/0081fbc4d9a25432ce31ac9807dea7ea0a151ab4))
* allow to set extra bones for the NetworkBones component ([7e7872d](https://github.com/PurrNet/PurrNet/commit/7e7872d60c733c7d135a43a95e7c3d6f0a4b70d3))
* attempting to make networktransform more performant ([6a065f2](https://github.com/PurrNet/PurrNet/commit/6a065f210f1d2a6ad238c6a057c512d53fa93b66))
* better error for unitask ([60e6ea2](https://github.com/PurrNet/PurrNet/commit/60e6ea222bc636c3f155d6c9329d0c62ba4cd069))
* cleanup issues ([36abd59](https://github.com/PurrNet/PurrNet/commit/36abd590f60caa6e79d7788e4871805b1014ab0e))
* compiler error for network module due to rework ([2a5a3e5](https://github.com/PurrNet/PurrNet/commit/2a5a3e5701011880acb580f392b2fdbbba673a88))
* compressed float equality failed, stick to storing raw value instead of float value ([2d04a76](https://github.com/PurrNet/PurrNet/commit/2d04a76d83fedd6a2c9f93e10d6c23338d92cb12))
* delta packing registration was faulty ([3db16b8](https://github.com/PurrNet/PurrNet/commit/3db16b844ee0e4379debed08f50f652506235586))
* disposable Array/Hashset collections missing delta packers ([71b1059](https://github.com/PurrNet/PurrNet/commit/71b1059df8a2b1ddc37a31f55128c0fd27d86019))
* disposable dictionary serialization fixes ([0a93474](https://github.com/PurrNet/PurrNet/commit/0a934746bda14c997c2aaaf3948fee32c81f8fd0))
* disposable list myers action application ([e5c3160](https://github.com/PurrNet/PurrNet/commit/e5c3160641a9bea983eb6ef5548359196066e86a))
* don't send irrelevant data for the NT ([315c331](https://github.com/PurrNet/PurrNet/commit/315c33131f45e9faa94e5047814438290f561869))
* GC when validating rpcs ([adeca8a](https://github.com/PurrNet/PurrNet/commit/adeca8a34f869cb39e1cd7700ecc2bbee22cb438))
* handle isServer scenario differently ([d7a930e](https://github.com/PurrNet/PurrNet/commit/d7a930e60060ecf1dbbc56159731420c4edcc047))
* hashset Create and obsolete constructor ([5c35273](https://github.com/PurrNet/PurrNet/commit/5c35273b030333786f95358601fc68cda7609520))
* host disconnecting was despawning other player owned objects due to bad cache ([52e1717](https://github.com/PurrNet/PurrNet/commit/52e171765f8e2b3834ed9ea3178979bbdabfcce4))
* if empty list ([d5c77f3](https://github.com/PurrNet/PurrNet/commit/d5c77f3a2d4dcabc145bdb6ef7ca8333ca99ec40))
* improved some packing for the NT ([e7eab45](https://github.com/PurrNet/PurrNet/commit/e7eab45db557b28cbe57e0f245809173f8697f6d))
* include local pos for child pieces ([4c67434](https://github.com/PurrNet/PurrNet/commit/4c67434b0fa854943a8ca84073931457b83f639c))
* include sceneid for spawn point provider ([2c64600](https://github.com/PurrNet/PurrNet/commit/2c646000874c61432005038c39ed9fe13543e7cd))
* Linked network prefabs logic added ([ba4a2e4](https://github.com/PurrNet/PurrNet/commit/ba4a2e4c4fa2e60e0d86ed7d8e6f3609fded1662))
* make sure any exceptions in the callbacks for synctypes dont break any flow AND that reset pool resets it's internal state ([de2a64c](https://github.com/PurrNet/PurrNet/commit/de2a64c216207ee4b495edab85fd253e3e3634f1))
* make sure disposable list is registered for dictionary ([34e461a](https://github.com/PurrNet/PurrNet/commit/34e461a6adea9617d29f41c740bda036a84b677e))
* make sure scene is valid when unloading ([0d7e8a3](https://github.com/PurrNet/PurrNet/commit/0d7e8a324237d8eec7faa8c7f3c0c0a743ff0553))
* make sure the packer has the proper data when communicating to others ([dc9f03c](https://github.com/PurrNet/PurrNet/commit/dc9f03cd39b184cd261016f187bd72a872a0e44e))
* messaging issue ([2a8c0cb](https://github.com/PurrNet/PurrNet/commit/2a8c0cbf684636d617f85a4eae141e0f0e52b305))
* more optimizations ([7e777de](https://github.com/PurrNet/PurrNet/commit/7e777ded30420e69a00fb6f7a020d6a3893baa0a))
* myers impl ([61b1929](https://github.com/PurrNet/PurrNet/commit/61b19299f17a9d48377a7ad73791c039a63bda45))
* naive delta packer for array and list ([4904eaa](https://github.com/PurrNet/PurrNet/commit/4904eaa69a2b5540b4d81b1a3fdf6000806a77f3))
* network transform module bug ([b6a0a5d](https://github.com/PurrNet/PurrNet/commit/b6a0a5d747f0369d9316123b8619e376f97572e8))
* NetworkTransform `ForceSync` was weird ([c2593fd](https://github.com/PurrNet/PurrNet/commit/c2593fdb3b3b8a019f851302df1016a711386a9f))
* new myers deltalist packer ([f8c6d05](https://github.com/PurrNet/PurrNet/commit/f8c6d058e8c192e3355fe7d768a2ef32825e0ca9))
* only adapt outside of editor ([cacc0c7](https://github.com/PurrNet/PurrNet/commit/cacc0c7bc9d501ee7bbbf65d1e1a028145182612))
* packer was rounding floats for CompressedFloat when it doesn't have to ([e968119](https://github.com/PurrNet/PurrNet/commit/e9681190fd2e6dcb1b91378757d8e6ce548d1942))
* packing for DisposableArray.cs ([1e5b4d2](https://github.com/PurrNet/PurrNet/commit/1e5b4d20907f09d45c03459c7396f8005dd12c7c))
* PurrTransport cache made changing master server a pain ([f642aff](https://github.com/PurrNet/PurrNet/commit/f642aff74b635176dcb1036b2a54f5909f42874b))
* purrtransport compiler error ([62713c5](https://github.com/PurrNet/PurrNet/commit/62713c51f0b792fc762a91725cdc469d00924c75))
* reflection getmethod failing ([568db6c](https://github.com/PurrNet/PurrNet/commit/568db6c39656b5aa315756127740db3c3b1a9f95))
* resolve hostname for the udp transport ([cc86356](https://github.com/PurrNet/PurrNet/commit/cc86356a681413e26bab57564c49d45dd6a8808d))
* set position after parenting ([f7d4dbf](https://github.com/PurrNet/PurrNet/commit/f7d4dbfdc7fe8a17f8dd9a15cf2a5393f0cac25a))
* some delta packing for spawn packet batches ([20388f8](https://github.com/PurrNet/PurrNet/commit/20388f8ef8d4c38c66d3a8fd09e008a4647a1499))
* some host issues with visibility rules ([29476a0](https://github.com/PurrNet/PurrNet/commit/29476a08b5626e1e5b84cb69c6498ab52af084db))
* some more `try catch` and reset whitelist/blacklist state when pool reset called ([5d4958b](https://github.com/PurrNet/PurrNet/commit/5d4958b1bfe948c7c77431740ce15912c92a1a08))
* some packing bugs ([fc9899a](https://github.com/PurrNet/PurrNet/commit/fc9899a1fc8fe9aad5e24aca35b5c429346572b9))
* some packing issues ([f43ee8a](https://github.com/PurrNet/PurrNet/commit/f43ee8a8220c908fda10f94bad6d78ed9c0967ca))
* sorting was backwards ([880a670](https://github.com/PurrNet/PurrNet/commit/880a67069036afed16757245dc6fdf2a58b336d8))
* spawn point provider pattern ([7e20abe](https://github.com/PurrNet/PurrNet/commit/7e20abe1a733511572bb231044dcc4985f38027f))
* steam errors when trying to use connection after closed ([ee4ed6a](https://github.com/PurrNet/PurrNet/commit/ee4ed6ab6c540d85aaf51ed0f5af29d43508e3ce))
* SyncTimer issues ([4d8e7f4](https://github.com/PurrNet/PurrNet/commit/4d8e7f4fc00fb9429eecb74727f73206d0d1350b))
* ulong packing ([c48f735](https://github.com/PurrNet/PurrNet/commit/c48f7354620cf346eba2ffa95ed529b93fb99517))
* unit tests circucal reference issue ([c9c0624](https://github.com/PurrNet/PurrNet/commit/c9c0624dc3715c6ed3f95b957b05c79046344fb2))
* unity version issues ([f8c90e2](https://github.com/PurrNet/PurrNet/commit/f8c90e2ddcd22c1a2a0dc94c427b9041619d1205))


### Features

* Added PlayerIdentity ([93ffe55](https://github.com/PurrNet/PurrNet/commit/93ffe558807b310e344348986d8aab4755893633))
* IStandaloneSerializable ([27e1733](https://github.com/PurrNet/PurrNet/commit/27e17337811855f0b0e5d486416bb1713cffe333))
* purrtransport udp support ([1a0ad4a](https://github.com/PurrNet/PurrNet/commit/1a0ad4ada2cc5becc5cf09473a9c8212fb8ac1ef))
* Run context guarded methods ([9309fb6](https://github.com/PurrNet/PurrNet/commit/9309fb64810a9595221e174d0ae21aa93ee93cce))

# [1.16.0-beta.56](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.55...v1.16.0-beta.56) (2025-10-16)


### Bug Fixes

* Added git checking to addon library ([336e590](https://github.com/PurrNet/PurrNet/commit/336e59027c71b3ed96a6dc6d8f4f5a699a6af213))

# [1.16.0-beta.55](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.54...v1.16.0-beta.55) (2025-10-10)


### Bug Fixes

* host disconnecting was despawning other player owned objects due to bad cache ([52e1717](https://github.com/PurrNet/PurrNet/commit/52e171765f8e2b3834ed9ea3178979bbdabfcce4))

# [1.16.0-beta.54](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.53...v1.16.0-beta.54) (2025-10-10)


### Bug Fixes

* only adapt outside of editor ([cacc0c7](https://github.com/PurrNet/PurrNet/commit/cacc0c7bc9d501ee7bbbf65d1e1a028145182612))

# [1.16.0-beta.53](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.52...v1.16.0-beta.53) (2025-10-10)


### Bug Fixes

* some more `try catch` and reset whitelist/blacklist state when pool reset called ([5d4958b](https://github.com/PurrNet/PurrNet/commit/5d4958b1bfe948c7c77431740ce15912c92a1a08))

# [1.16.0-beta.52](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.51...v1.16.0-beta.52) (2025-10-10)


### Bug Fixes

* make sure any exceptions in the callbacks for synctypes dont break any flow AND that reset pool resets it's internal state ([de2a64c](https://github.com/PurrNet/PurrNet/commit/de2a64c216207ee4b495edab85fd253e3e3634f1))

# [1.16.0-beta.51](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.50...v1.16.0-beta.51) (2025-10-08)


### Bug Fixes

* disposable Array/Hashset collections missing delta packers ([71b1059](https://github.com/PurrNet/PurrNet/commit/71b1059df8a2b1ddc37a31f55128c0fd27d86019))

# [1.16.0-beta.50](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.49...v1.16.0-beta.50) (2025-10-08)


### Bug Fixes

* hashset Create and obsolete constructor ([5c35273](https://github.com/PurrNet/PurrNet/commit/5c35273b030333786f95358601fc68cda7609520))

# [1.16.0-beta.49](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.48...v1.16.0-beta.49) (2025-10-07)


### Bug Fixes

* disposable list myers action application ([e5c3160](https://github.com/PurrNet/PurrNet/commit/e5c3160641a9bea983eb6ef5548359196066e86a))

# [1.16.0-beta.48](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.47...v1.16.0-beta.48) (2025-10-07)


### Bug Fixes

* delta packing registration was faulty ([3db16b8](https://github.com/PurrNet/PurrNet/commit/3db16b844ee0e4379debed08f50f652506235586))

# [1.16.0-beta.47](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.46...v1.16.0-beta.47) (2025-10-07)


### Bug Fixes

* ulong packing ([c48f735](https://github.com/PurrNet/PurrNet/commit/c48f7354620cf346eba2ffa95ed529b93fb99517))

# [1.16.0-beta.46](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.45...v1.16.0-beta.46) (2025-10-07)


### Bug Fixes

* packer was rounding floats for CompressedFloat when it doesn't have to ([e968119](https://github.com/PurrNet/PurrNet/commit/e9681190fd2e6dcb1b91378757d8e6ce548d1942))

# [1.16.0-beta.45](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.44...v1.16.0-beta.45) (2025-10-07)


### Bug Fixes

* compressed float equality failed, stick to storing raw value instead of float value ([2d04a76](https://github.com/PurrNet/PurrNet/commit/2d04a76d83fedd6a2c9f93e10d6c23338d92cb12))

# [1.16.0-beta.44](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.43...v1.16.0-beta.44) (2025-10-07)


### Bug Fixes

* add debugging scripting symbol for delta compression that packs extra data to let you know of issues ([2c96204](https://github.com/PurrNet/PurrNet/commit/2c9620425c2ae43e545bbc04ed94205f02e75d6e))

# [1.16.0-beta.43](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.42...v1.16.0-beta.43) (2025-10-05)


### Bug Fixes

* unit tests circucal reference issue ([c9c0624](https://github.com/PurrNet/PurrNet/commit/c9c0624dc3715c6ed3f95b957b05c79046344fb2))

# [1.16.0-beta.42](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.41...v1.16.0-beta.42) (2025-10-04)


### Bug Fixes

* disposable dictionary serialization fixes ([0a93474](https://github.com/PurrNet/PurrNet/commit/0a934746bda14c997c2aaaf3948fee32c81f8fd0))

# [1.16.0-beta.41](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.40...v1.16.0-beta.41) (2025-10-02)


### Bug Fixes

* make sure the packer has the proper data when communicating to others ([dc9f03c](https://github.com/PurrNet/PurrNet/commit/dc9f03cd39b184cd261016f187bd72a872a0e44e))

# [1.16.0-beta.40](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.39...v1.16.0-beta.40) (2025-09-30)


### Bug Fixes

* packing for DisposableArray.cs ([1e5b4d2](https://github.com/PurrNet/PurrNet/commit/1e5b4d20907f09d45c03459c7396f8005dd12c7c))

# [1.16.0-beta.39](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.38...v1.16.0-beta.39) (2025-09-29)


### Bug Fixes

* make sure disposable list is registered for dictionary ([34e461a](https://github.com/PurrNet/PurrNet/commit/34e461a6adea9617d29f41c740bda036a84b677e))

# [1.16.0-beta.38](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.37...v1.16.0-beta.38) (2025-09-28)


### Bug Fixes

* attempting to make networktransform more performant ([6a065f2](https://github.com/PurrNet/PurrNet/commit/6a065f210f1d2a6ad238c6a057c512d53fa93b66))

# [1.16.0-beta.37](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.36...v1.16.0-beta.37) (2025-09-28)


### Bug Fixes

* sorting was backwards ([880a670](https://github.com/PurrNet/PurrNet/commit/880a67069036afed16757245dc6fdf2a58b336d8))

# [1.16.0-beta.36](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.35...v1.16.0-beta.36) (2025-09-28)


### Bug Fixes

* allow to prioritize non generated packers more explicitly ([0081fbc](https://github.com/PurrNet/PurrNet/commit/0081fbc4d9a25432ce31ac9807dea7ea0a151ab4))

# [1.16.0-beta.35](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.34...v1.16.0-beta.35) (2025-09-26)


### Bug Fixes

* new myers deltalist packer ([f8c6d05](https://github.com/PurrNet/PurrNet/commit/f8c6d058e8c192e3355fe7d768a2ef32825e0ca9))

# [1.16.0-beta.34](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.33...v1.16.0-beta.34) (2025-09-25)


### Bug Fixes

* myers impl ([61b1929](https://github.com/PurrNet/PurrNet/commit/61b19299f17a9d48377a7ad73791c039a63bda45))

# [1.16.0-beta.33](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.32...v1.16.0-beta.33) (2025-09-25)


### Bug Fixes

* some packing bugs ([fc9899a](https://github.com/PurrNet/PurrNet/commit/fc9899a1fc8fe9aad5e24aca35b5c429346572b9))

# [1.16.0-beta.32](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.31...v1.16.0-beta.32) (2025-09-25)


### Bug Fixes

* better error for unitask ([60e6ea2](https://github.com/PurrNet/PurrNet/commit/60e6ea222bc636c3f155d6c9329d0c62ba4cd069))

# [1.16.0-beta.31](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.30...v1.16.0-beta.31) (2025-09-25)


### Bug Fixes

* include sceneid for spawn point provider ([2c64600](https://github.com/PurrNet/PurrNet/commit/2c646000874c61432005038c39ed9fe13543e7cd))

# [1.16.0-beta.30](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.29...v1.16.0-beta.30) (2025-09-25)


### Bug Fixes

* if empty list ([d5c77f3](https://github.com/PurrNet/PurrNet/commit/d5c77f3a2d4dcabc145bdb6ef7ca8333ca99ec40))

# [1.16.0-beta.29](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.28...v1.16.0-beta.29) (2025-09-25)


### Bug Fixes

* messaging issue ([2a8c0cb](https://github.com/PurrNet/PurrNet/commit/2a8c0cbf684636d617f85a4eae141e0f0e52b305))

# [1.16.0-beta.28](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.27...v1.16.0-beta.28) (2025-09-25)


### Bug Fixes

* spawn point provider pattern ([7e20abe](https://github.com/PurrNet/PurrNet/commit/7e20abe1a733511572bb231044dcc4985f38027f))

# [1.16.0-beta.27](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.26...v1.16.0-beta.27) (2025-09-25)


### Bug Fixes

* more optimizations ([7e777de](https://github.com/PurrNet/PurrNet/commit/7e777ded30420e69a00fb6f7a020d6a3893baa0a))

# [1.16.0-beta.26](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.25...v1.16.0-beta.26) (2025-09-25)


### Bug Fixes

* improved some packing for the NT ([e7eab45](https://github.com/PurrNet/PurrNet/commit/e7eab45db557b28cbe57e0f245809173f8697f6d))

# [1.16.0-beta.25](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.24...v1.16.0-beta.25) (2025-09-25)


### Bug Fixes

* +disposable array ([4c92504](https://github.com/PurrNet/PurrNet/commit/4c925042940abf14b839d1b5b6a7063a56be34b2))
* allow to set extra bones for the NetworkBones component ([7e7872d](https://github.com/PurrNet/PurrNet/commit/7e7872d60c733c7d135a43a95e7c3d6f0a4b70d3))
* some packing issues ([f43ee8a](https://github.com/PurrNet/PurrNet/commit/f43ee8a8220c908fda10f94bad6d78ed9c0967ca))

# [1.16.0-beta.24](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.23...v1.16.0-beta.24) (2025-09-23)


### Bug Fixes

* Added statistics manager display target options ([75d095f](https://github.com/PurrNet/PurrNet/commit/75d095ff1881ed47d836c9af63baf21c90bc09e9))

# [1.16.0-beta.23](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.22...v1.16.0-beta.23) (2025-09-23)


### Bug Fixes

* compiler error for network module due to rework ([2a5a3e5](https://github.com/PurrNet/PurrNet/commit/2a5a3e5701011880acb580f392b2fdbbba673a88))

# [1.16.0-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.21...v1.16.0-beta.22) (2025-09-23)


### Bug Fixes

* GC when validating rpcs ([adeca8a](https://github.com/PurrNet/PurrNet/commit/adeca8a34f869cb39e1cd7700ecc2bbee22cb438))

# [1.16.0-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.20...v1.16.0-beta.21) (2025-09-22)


### Bug Fixes

* Linked network prefabs logic added ([ba4a2e4](https://github.com/PurrNet/PurrNet/commit/ba4a2e4c4fa2e60e0d86ed7d8e6f3609fded1662))

# [1.16.0-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.19...v1.16.0-beta.20) (2025-09-22)


### Bug Fixes

* make sure scene is valid when unloading ([0d7e8a3](https://github.com/PurrNet/PurrNet/commit/0d7e8a324237d8eec7faa8c7f3c0c0a743ff0553))

# [1.16.0-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.18...v1.16.0-beta.19) (2025-09-21)


### Bug Fixes

* reflection getmethod failing ([568db6c](https://github.com/PurrNet/PurrNet/commit/568db6c39656b5aa315756127740db3c3b1a9f95))

# [1.16.0-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.17...v1.16.0-beta.18) (2025-09-21)


### Bug Fixes

* Added PurrChat link to toolbar ([72d3d58](https://github.com/PurrNet/PurrNet/commit/72d3d58cf6be44a925837f736c49b7a1af92e93b))

# [1.16.0-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.16...v1.16.0-beta.17) (2025-09-17)


### Bug Fixes

* some host issues with visibility rules ([29476a0](https://github.com/PurrNet/PurrNet/commit/29476a08b5626e1e5b84cb69c6498ab52af084db))

# [1.16.0-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.15...v1.16.0-beta.16) (2025-09-17)


### Features

* Run context guarded methods ([9309fb6](https://github.com/PurrNet/PurrNet/commit/9309fb64810a9595221e174d0ae21aa93ee93cce))

# [1.16.0-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.14...v1.16.0-beta.15) (2025-09-14)


### Bug Fixes

* set position after parenting ([f7d4dbf](https://github.com/PurrNet/PurrNet/commit/f7d4dbfdc7fe8a17f8dd9a15cf2a5393f0cac25a))

# [1.16.0-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.13...v1.16.0-beta.14) (2025-09-10)


### Bug Fixes

* steam errors when trying to use connection after closed ([ee4ed6a](https://github.com/PurrNet/PurrNet/commit/ee4ed6ab6c540d85aaf51ed0f5af29d43508e3ce))

# [1.16.0-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.12...v1.16.0-beta.13) (2025-09-10)


### Bug Fixes

* include local pos for child pieces ([4c67434](https://github.com/PurrNet/PurrNet/commit/4c67434b0fa854943a8ca84073931457b83f639c))

# [1.16.0-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.11...v1.16.0-beta.12) (2025-09-09)


### Bug Fixes

* naive delta packer for array and list ([4904eaa](https://github.com/PurrNet/PurrNet/commit/4904eaa69a2b5540b4d81b1a3fdf6000806a77f3))

# [1.16.0-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.10...v1.16.0-beta.11) (2025-09-07)


### Bug Fixes

* some delta packing for spawn packet batches ([20388f8](https://github.com/PurrNet/PurrNet/commit/20388f8ef8d4c38c66d3a8fd09e008a4647a1499))

# [1.16.0-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.9...v1.16.0-beta.10) (2025-09-05)


### Features

* Added PlayerIdentity ([93ffe55](https://github.com/PurrNet/PurrNet/commit/93ffe558807b310e344348986d8aab4755893633))

# [1.16.0-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.8...v1.16.0-beta.9) (2025-09-05)


### Bug Fixes

* network transform module bug ([b6a0a5d](https://github.com/PurrNet/PurrNet/commit/b6a0a5d747f0369d9316123b8619e376f97572e8))

# [1.16.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.7...v1.16.0-beta.8) (2025-09-04)


### Bug Fixes

* purrtransport compiler error ([62713c5](https://github.com/PurrNet/PurrNet/commit/62713c51f0b792fc762a91725cdc469d00924c75))

# [1.16.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.6...v1.16.0-beta.7) (2025-09-04)


### Bug Fixes

* don't send irrelevant data for the NT ([315c331](https://github.com/PurrNet/PurrNet/commit/315c33131f45e9faa94e5047814438290f561869))

# [1.16.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.5...v1.16.0-beta.6) (2025-09-03)


### Bug Fixes

* resolve hostname for the udp transport ([cc86356](https://github.com/PurrNet/PurrNet/commit/cc86356a681413e26bab57564c49d45dd6a8808d))

# [1.16.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.4...v1.16.0-beta.5) (2025-09-02)


### Bug Fixes

* handle isServer scenario differently ([d7a930e](https://github.com/PurrNet/PurrNet/commit/d7a930e60060ecf1dbbc56159731420c4edcc047))

# [1.16.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.3...v1.16.0-beta.4) (2025-09-02)


### Bug Fixes

* NetworkTransform `ForceSync` was weird ([c2593fd](https://github.com/PurrNet/PurrNet/commit/c2593fdb3b3b8a019f851302df1016a711386a9f))

# [1.16.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.2...v1.16.0-beta.3) (2025-09-02)


### Features

* purrtransport udp support ([1a0ad4a](https://github.com/PurrNet/PurrNet/commit/1a0ad4ada2cc5becc5cf09473a9c8212fb8ac1ef))

# [1.16.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.1...v1.16.0-beta.2) (2025-09-02)


### Bug Fixes

* allow network animator to reconcile time ([caa62bc](https://github.com/PurrNet/PurrNet/commit/caa62bc81078a38cf5381dad5f888e6186ee3089))

# [1.16.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.15.1-beta.5...v1.16.0-beta.1) (2025-09-01)


### Features

* IStandaloneSerializable ([27e1733](https://github.com/PurrNet/PurrNet/commit/27e17337811855f0b0e5d486416bb1713cffe333))

## [1.15.1-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.15.1-beta.4...v1.15.1-beta.5) (2025-08-28)


### Bug Fixes

* add `HierarchyV2.onPreSpawn` static event ([0c02749](https://github.com/PurrNet/PurrNet/commit/0c0274922d2cfb1db576c4bb4fbfa4d1e73f50f6))

## [1.15.1-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.15.1-beta.3...v1.15.1-beta.4) (2025-08-28)


### Bug Fixes

* SyncTimer issues ([4d8e7f4](https://github.com/PurrNet/PurrNet/commit/4d8e7f4fc00fb9429eecb74727f73206d0d1350b))

## [1.15.1-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.15.1-beta.2...v1.15.1-beta.3) (2025-08-28)


### Bug Fixes

* unity version issues ([f8c90e2](https://github.com/PurrNet/PurrNet/commit/f8c90e2ddcd22c1a2a0dc94c427b9041619d1205))

## [1.15.1-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.15.1-beta.1...v1.15.1-beta.2) (2025-08-27)


### Bug Fixes

* PurrTransport cache made changing master server a pain ([f642aff](https://github.com/PurrNet/PurrNet/commit/f642aff74b635176dcb1036b2a54f5909f42874b))

## [1.15.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.15.0...v1.15.1-beta.1) (2025-08-25)


### Bug Fixes

* cleanup issues ([36abd59](https://github.com/PurrNet/PurrNet/commit/36abd590f60caa6e79d7788e4871805b1014ab0e))

# [1.15.0](https://github.com/PurrNet/PurrNet/compare/v1.14.1...v1.15.0) (2025-08-25)


### Bug Fixes

* allow to filter purrnet's scene object discovery ([522ef9d](https://github.com/PurrNet/PurrNet/commit/522ef9d042983d83d886c63d05342a7a704d0f50))
* allow to skip scene auto spawning ([204987c](https://github.com/PurrNet/PurrNet/commit/204987c50ded5d0b76dc5a83a6c7a8e264a95c80))
* avoid reading data if bones aren't ready ([fca1a62](https://github.com/PurrNet/PurrNet/commit/fca1a62a2bba25bf840853afdf3bc7915e5d569d))
* better internal packer resizing calc ([ea6f39d](https://github.com/PurrNet/PurrNet/commit/ea6f39df6c0f66e242e183c083de1c6788f586db))
* cleanup modules ([729fc3a](https://github.com/PurrNet/PurrNet/commit/729fc3a8330aef563fe1c4d2d15f583999506403))
* dispose bones when destroying object ([f92b774](https://github.com/PurrNet/PurrNet/commit/f92b774e412c93c6c4c051bc23744ad66d84fd8e))
* don't put `skipSceneAutoSpawning` in the pool ([e84f639](https://github.com/PurrNet/PurrNet/commit/e84f639b77fcea0922f252768de8812bf8f77857))
* filter shouldn't be as broad as a GO ([9c1597f](https://github.com/PurrNet/PurrNet/commit/9c1597faadb2e7c208d1a3e3c2e835ce24e9114b))
* networkbones courtesy of Resolute Games ([896a018](https://github.com/PurrNet/PurrNet/commit/896a01876af0d372c6b3723004e1e78bf99fa9e3))
* pack unity LayerMask ([a23e8dd](https://github.com/PurrNet/PurrNet/commit/a23e8ddc64b88ed17142b63eb07449f89f88ef1a))
* scene load events ([63dbc5c](https://github.com/PurrNet/PurrNet/commit/63dbc5cbbc306c9175230b37209a98c3397cc07c))
* UDP transport reconnection ([c15c6a5](https://github.com/PurrNet/PurrNet/commit/c15c6a5704c4fc83f990de0b42533fae77b7fb3c))
* unity 6 color thingy ([4344e4e](https://github.com/PurrNet/PurrNet/commit/4344e4e3026351967944c00c19a98c5fac29d3aa))


### Features

* add CompressedVector2 for 2D vector compression ([57a0213](https://github.com/PurrNet/PurrNet/commit/57a021325b3734f60ba37ed4ab1eee4703594501))
* Add implicit conversion operators for CompressedVector3 <-> Vector2 ([b44f7b5](https://github.com/PurrNet/PurrNet/commit/b44f7b5a2463aa7ea50affd6339244d1196f6885))
* allow to enable/disable purr buttons ([7d37c56](https://github.com/PurrNet/PurrNet/commit/7d37c5693171ce82217cdd978a4084c53323effa))
* endian checks ([5ebbe9f](https://github.com/PurrNet/PurrNet/commit/5ebbe9f220b9790413f42e72151629ec4788ce40))

# [1.15.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.15.0-beta.7...v1.15.0-beta.8) (2025-08-24)


### Bug Fixes

* cleanup modules ([729fc3a](https://github.com/PurrNet/PurrNet/commit/729fc3a8330aef563fe1c4d2d15f583999506403))

# [1.15.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.15.0-beta.6...v1.15.0-beta.7) (2025-08-23)


### Bug Fixes

* don't put `skipSceneAutoSpawning` in the pool ([e84f639](https://github.com/PurrNet/PurrNet/commit/e84f639b77fcea0922f252768de8812bf8f77857))

# [1.15.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.15.0-beta.5...v1.15.0-beta.6) (2025-08-23)


### Bug Fixes

* better internal packer resizing calc ([ea6f39d](https://github.com/PurrNet/PurrNet/commit/ea6f39df6c0f66e242e183c083de1c6788f586db))

# [1.15.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.15.0-beta.4...v1.15.0-beta.5) (2025-08-23)


### Features

* endian checks ([5ebbe9f](https://github.com/PurrNet/PurrNet/commit/5ebbe9f220b9790413f42e72151629ec4788ce40))

# [1.15.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.15.0-beta.3...v1.15.0-beta.4) (2025-08-23)


### Features

* allow to enable/disable purr buttons ([7d37c56](https://github.com/PurrNet/PurrNet/commit/7d37c5693171ce82217cdd978a4084c53323effa))

# [1.15.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.15.0-beta.2...v1.15.0-beta.3) (2025-08-23)


### Bug Fixes

* scene load events ([63dbc5c](https://github.com/PurrNet/PurrNet/commit/63dbc5cbbc306c9175230b37209a98c3397cc07c))

# [1.15.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.15.0-beta.1...v1.15.0-beta.2) (2025-08-23)


### Bug Fixes

* allow to skip scene auto spawning ([204987c](https://github.com/PurrNet/PurrNet/commit/204987c50ded5d0b76dc5a83a6c7a8e264a95c80))

# [1.15.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.14.2-beta.8...v1.15.0-beta.1) (2025-08-22)


### Features

* add CompressedVector2 for 2D vector compression ([57a0213](https://github.com/PurrNet/PurrNet/commit/57a021325b3734f60ba37ed4ab1eee4703594501))
* Add implicit conversion operators for CompressedVector3 <-> Vector2 ([b44f7b5](https://github.com/PurrNet/PurrNet/commit/b44f7b5a2463aa7ea50affd6339244d1196f6885))

## [1.14.2-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.14.2-beta.7...v1.14.2-beta.8) (2025-08-22)


### Bug Fixes

* avoid reading data if bones aren't ready ([fca1a62](https://github.com/PurrNet/PurrNet/commit/fca1a62a2bba25bf840853afdf3bc7915e5d569d))

## [1.14.2-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.14.2-beta.6...v1.14.2-beta.7) (2025-08-21)


### Bug Fixes

* filter shouldn't be as broad as a GO ([9c1597f](https://github.com/PurrNet/PurrNet/commit/9c1597faadb2e7c208d1a3e3c2e835ce24e9114b))

## [1.14.2-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.14.2-beta.5...v1.14.2-beta.6) (2025-08-21)


### Bug Fixes

* allow to filter purrnet's scene object discovery ([522ef9d](https://github.com/PurrNet/PurrNet/commit/522ef9d042983d83d886c63d05342a7a704d0f50))

## [1.14.2-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.14.2-beta.4...v1.14.2-beta.5) (2025-08-20)


### Bug Fixes

* UDP transport reconnection ([c15c6a5](https://github.com/PurrNet/PurrNet/commit/c15c6a5704c4fc83f990de0b42533fae77b7fb3c))

## [1.14.2-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.14.2-beta.3...v1.14.2-beta.4) (2025-08-20)


### Bug Fixes

* unity 6 color thingy ([4344e4e](https://github.com/PurrNet/PurrNet/commit/4344e4e3026351967944c00c19a98c5fac29d3aa))

## [1.14.2-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.14.2-beta.2...v1.14.2-beta.3) (2025-08-19)


### Bug Fixes

* pack unity LayerMask ([a23e8dd](https://github.com/PurrNet/PurrNet/commit/a23e8ddc64b88ed17142b63eb07449f89f88ef1a))

## [1.14.2-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.14.2-beta.1...v1.14.2-beta.2) (2025-08-18)


### Bug Fixes

* networkbones courtesy of Resolute Games ([896a018](https://github.com/PurrNet/PurrNet/commit/896a01876af0d372c6b3723004e1e78bf99fa9e3))

## [1.14.2-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.14.1...v1.14.2-beta.1) (2025-08-17)


### Bug Fixes

* dispose bones when destroying object ([f92b774](https://github.com/PurrNet/PurrNet/commit/f92b774e412c93c6c4c051bc23744ad66d84fd8e))

## [1.14.1](https://github.com/PurrNet/PurrNet/compare/v1.14.0...v1.14.1) (2025-08-16)


### Bug Fixes

* Addon library fixed for manifest handling ([7b13f01](https://github.com/PurrNet/PurrNet/commit/7b13f013218777dc32b4536539915a21411e8e2c))
* Addon library image request handling improved ([49eccf7](https://github.com/PurrNet/PurrNet/commit/49eccf794307569694ad47d794a70ccca02cd322))
* buffer settings for bones ([f4af0eb](https://github.com/PurrNet/PurrNet/commit/f4af0ebbace94885acd8c51e5b9c20ad32d1ce6b))
* NetworkBones adjustments ([5a61a86](https://github.com/PurrNet/PurrNet/commit/5a61a869a55c057609b05773acc9594908ea433c))

## [1.14.1-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.14.1-beta.3...v1.14.1-beta.4) (2025-08-16)


### Bug Fixes

* Addon library image request handling improved ([49eccf7](https://github.com/PurrNet/PurrNet/commit/49eccf794307569694ad47d794a70ccca02cd322))

## [1.14.1-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.14.1-beta.2...v1.14.1-beta.3) (2025-08-16)


### Bug Fixes

* Addon library fixed for manifest handling ([7b13f01](https://github.com/PurrNet/PurrNet/commit/7b13f013218777dc32b4536539915a21411e8e2c))

## [1.14.1-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.14.1-beta.1...v1.14.1-beta.2) (2025-08-16)


### Bug Fixes

* buffer settings for bones ([f4af0eb](https://github.com/PurrNet/PurrNet/commit/f4af0ebbace94885acd8c51e5b9c20ad32d1ce6b))

## [1.14.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.14.0...v1.14.1-beta.1) (2025-08-16)


### Bug Fixes

* NetworkBones adjustments ([5a61a86](https://github.com/PurrNet/PurrNet/commit/5a61a869a55c057609b05773acc9594908ea433c))

# [1.14.0](https://github.com/PurrNet/PurrNet/compare/v1.13.3...v1.14.0) (2025-08-15)


### Bug Fixes

* actual il fixes ([6a8176e](https://github.com/PurrNet/PurrNet/commit/6a8176e99ebb8ae73c7a6cd99d98001cadfe2248))
* allow DontPack to be at the type level ([354f271](https://github.com/PurrNet/PurrNet/commit/354f27178bc420b279c50c79a01e7c02fb09e2b5))
* allow for null values when reading classes with inheritance ([86db192](https://github.com/PurrNet/PurrNet/commit/86db1929d3d81367e362effb6537fa28abc511ac))
* allow for value modifiers for the delta module ([c0ddf66](https://github.com/PurrNet/PurrNet/commit/c0ddf665067973d5d28c2c63ad01a4a8c941ce1f))
* also cleanup on destroy ([2d47451](https://github.com/PurrNet/PurrNet/commit/2d47451772812bf5caf09c6c7af4e69d602c3485))
* delta list packing ([7392b4b](https://github.com/PurrNet/PurrNet/commit/7392b4b6e276ae91cde72dd2b3c509194cc1e1ab))
* disposable list packer issue ([23598b9](https://github.com/PurrNet/PurrNet/commit/23598b9192b0f0402d1487d7e84644d14b4f97f4))
* dont create a server object until we need it since it causes issues ([801db42](https://github.com/PurrNet/PurrNet/commit/801db425600b41bf6eb0b9fc295edb9d082324b2))
* if we hit cleanup from `OnDisable` force close the connection ([318c5ed](https://github.com/PurrNet/PurrNet/commit/318c5edfd59fe0af734821cdd23c7dadde524b69))
* il error ([5e27ed6](https://github.com/PurrNet/PurrNet/commit/5e27ed62c9bfdfe0d3644e2e6dd5ae14d09018b7))
* IL generic resolving ([9f04291](https://github.com/PurrNet/PurrNet/commit/9f042912b8628a53384195d30c051f8974fa1af9))
* make `DontPack` attribute skip creating generators entirely if at the class level ([e18bf9c](https://github.com/PurrNet/PurrNet/commit/e18bf9c2e77658f33e9d8b1756dffd050306e33e))
* mark manual spawns such that we handle them differently (like not populating observers automatically) ([2dc77b8](https://github.com/PurrNet/PurrNet/commit/2dc77b8d8bc358902a5d407ddd8dcc5475de91f6))
* more modifier delta packing fixes ([c604c50](https://github.com/PurrNet/PurrNet/commit/c604c50fd946ff5460fe63e7921e455885ea42eb))
* more robust register calling and skipping of assemblies that don't refrence the purrnet assembly ([5daec62](https://github.com/PurrNet/PurrNet/commit/5daec625ecb0c3cb405162afc2bcdb772f170d81))
* return value of ValueModifier wasnt necessary ([ed0d668](https://github.com/PurrNet/PurrNet/commit/ed0d668b7ade468ca9024527d7bae92a2c5980d0))
* rework how RPC are called ([0f3c4f1](https://github.com/PurrNet/PurrNet/commit/0f3c4f1cfe992a89ca719afe53fd6e167c840d72))
* Statistics manager versioning position fix ([1539c54](https://github.com/PurrNet/PurrNet/commit/1539c54a46659045893b82665387e52c6bfaca51))
* Sync List null handling issue ([5704d83](https://github.com/PurrNet/PurrNet/commit/5704d83add83ced6dbab7138cfb3ea0d8f09fe8e))
* syncvar equality check regression ([d280ed5](https://github.com/PurrNet/PurrNet/commit/d280ed58bb1109a4f4622ff393271edc4da4e9ed))
* testing NetworkBones component ([7bcd4d5](https://github.com/PurrNet/PurrNet/commit/7bcd4d598cfca35fbfd19d569fd3ebe2cfdfe40b))
* type error deeper error message ([06904ef](https://github.com/PurrNet/PurrNet/commit/06904ef17432e96fb2ea86705a0bfcbf3f173e46))
* UnityProxy fails if manager doesn't have prefab provider ([fd2e674](https://github.com/PurrNet/PurrNet/commit/fd2e6746c5c4798430c55c4e41d1ee1fe3806a04))
* write/read with modifier bad history ([b8a7e3c](https://github.com/PurrNet/PurrNet/commit/b8a7e3c2ee6fc04f49279fbbed66db7783793749))


### Features

* add Packer.HasPacker and DeltaPacker.HasPacker ([a643b7b](https://github.com/PurrNet/PurrNet/commit/a643b7b30895e2f3be34c925d77b2282a456be8d))
* allow to force ipv4 for web transport ([83756ea](https://github.com/PurrNet/PurrNet/commit/83756eae66255ae2e1abac7e0009690876f1e59b))
* allow to not delta compress certain fields ([f320274](https://github.com/PurrNet/PurrNet/commit/f32027485614946ff34d65e8cfc5f730304fe402))

# [1.14.0-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.21...v1.14.0-beta.22) (2025-08-14)


### Bug Fixes

* testing NetworkBones component ([7bcd4d5](https://github.com/PurrNet/PurrNet/commit/7bcd4d598cfca35fbfd19d569fd3ebe2cfdfe40b))

# [1.14.0-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.20...v1.14.0-beta.21) (2025-08-14)


### Bug Fixes

* allow for null values when reading classes with inheritance ([86db192](https://github.com/PurrNet/PurrNet/commit/86db1929d3d81367e362effb6537fa28abc511ac))

# [1.14.0-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.19...v1.14.0-beta.20) (2025-08-14)


### Bug Fixes

* dont create a server object until we need it since it causes issues ([801db42](https://github.com/PurrNet/PurrNet/commit/801db425600b41bf6eb0b9fc295edb9d082324b2))

# [1.14.0-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.18...v1.14.0-beta.19) (2025-08-14)


### Bug Fixes

* if we hit cleanup from `OnDisable` force close the connection ([318c5ed](https://github.com/PurrNet/PurrNet/commit/318c5edfd59fe0af734821cdd23c7dadde524b69))

# [1.14.0-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.17...v1.14.0-beta.18) (2025-08-14)


### Bug Fixes

* type error deeper error message ([06904ef](https://github.com/PurrNet/PurrNet/commit/06904ef17432e96fb2ea86705a0bfcbf3f173e46))

# [1.14.0-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.16...v1.14.0-beta.17) (2025-08-13)


### Bug Fixes

* delta list packing ([7392b4b](https://github.com/PurrNet/PurrNet/commit/7392b4b6e276ae91cde72dd2b3c509194cc1e1ab))

# [1.14.0-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.15...v1.14.0-beta.16) (2025-08-13)


### Bug Fixes

* more modifier delta packing fixes ([c604c50](https://github.com/PurrNet/PurrNet/commit/c604c50fd946ff5460fe63e7921e455885ea42eb))

# [1.14.0-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.14...v1.14.0-beta.15) (2025-08-13)


### Bug Fixes

* write/read with modifier bad history ([b8a7e3c](https://github.com/PurrNet/PurrNet/commit/b8a7e3c2ee6fc04f49279fbbed66db7783793749))

# [1.14.0-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.13...v1.14.0-beta.14) (2025-08-11)


### Bug Fixes

* mark manual spawns such that we handle them differently (like not populating observers automatically) ([2dc77b8](https://github.com/PurrNet/PurrNet/commit/2dc77b8d8bc358902a5d407ddd8dcc5475de91f6))

# [1.14.0-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.12...v1.14.0-beta.13) (2025-08-11)


### Bug Fixes

* IL generic resolving ([9f04291](https://github.com/PurrNet/PurrNet/commit/9f042912b8628a53384195d30c051f8974fa1af9))

# [1.14.0-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.11...v1.14.0-beta.12) (2025-08-10)


### Bug Fixes

* make `DontPack` attribute skip creating generators entirely if at the class level ([e18bf9c](https://github.com/PurrNet/PurrNet/commit/e18bf9c2e77658f33e9d8b1756dffd050306e33e))

# [1.14.0-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.10...v1.14.0-beta.11) (2025-08-10)


### Bug Fixes

* return value of ValueModifier wasnt necessary ([ed0d668](https://github.com/PurrNet/PurrNet/commit/ed0d668b7ade468ca9024527d7bae92a2c5980d0))

# [1.14.0-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.9...v1.14.0-beta.10) (2025-08-10)


### Bug Fixes

* allow for value modifiers for the delta module ([c0ddf66](https://github.com/PurrNet/PurrNet/commit/c0ddf665067973d5d28c2c63ad01a4a8c941ce1f))

# [1.14.0-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.8...v1.14.0-beta.9) (2025-08-10)


### Bug Fixes

* Sync List null handling issue ([5704d83](https://github.com/PurrNet/PurrNet/commit/5704d83add83ced6dbab7138cfb3ea0d8f09fe8e))

# [1.14.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.7...v1.14.0-beta.8) (2025-08-06)


### Bug Fixes

* actual il fixes ([6a8176e](https://github.com/PurrNet/PurrNet/commit/6a8176e99ebb8ae73c7a6cd99d98001cadfe2248))
* il error ([5e27ed6](https://github.com/PurrNet/PurrNet/commit/5e27ed62c9bfdfe0d3644e2e6dd5ae14d09018b7))

# [1.14.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.6...v1.14.0-beta.7) (2025-08-05)


### Bug Fixes

* syncvar equality check regression ([d280ed5](https://github.com/PurrNet/PurrNet/commit/d280ed58bb1109a4f4622ff393271edc4da4e9ed))

# [1.14.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.5...v1.14.0-beta.6) (2025-08-05)


### Features

* allow to force ipv4 for web transport ([83756ea](https://github.com/PurrNet/PurrNet/commit/83756eae66255ae2e1abac7e0009690876f1e59b))

# [1.14.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.4...v1.14.0-beta.5) (2025-08-05)


### Bug Fixes

* also cleanup on destroy ([2d47451](https://github.com/PurrNet/PurrNet/commit/2d47451772812bf5caf09c6c7af4e69d602c3485))

# [1.14.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.3...v1.14.0-beta.4) (2025-08-05)


### Bug Fixes

* UnityProxy fails if manager doesn't have prefab provider ([fd2e674](https://github.com/PurrNet/PurrNet/commit/fd2e6746c5c4798430c55c4e41d1ee1fe3806a04))

# [1.14.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.2...v1.14.0-beta.3) (2025-08-05)


### Features

* add Packer.HasPacker and DeltaPacker.HasPacker ([a643b7b](https://github.com/PurrNet/PurrNet/commit/a643b7b30895e2f3be34c925d77b2282a456be8d))

# [1.14.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.1...v1.14.0-beta.2) (2025-08-05)


### Bug Fixes

* disposable list packer issue ([23598b9](https://github.com/PurrNet/PurrNet/commit/23598b9192b0f0402d1487d7e84644d14b4f97f4))

# [1.14.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.13.4-beta.4...v1.14.0-beta.1) (2025-08-05)


### Features

* allow to not delta compress certain fields ([f320274](https://github.com/PurrNet/PurrNet/commit/f32027485614946ff34d65e8cfc5f730304fe402))

## [1.13.4-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.13.4-beta.3...v1.13.4-beta.4) (2025-08-05)


### Bug Fixes

* allow DontPack to be at the type level ([354f271](https://github.com/PurrNet/PurrNet/commit/354f27178bc420b279c50c79a01e7c02fb09e2b5))

## [1.13.4-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.13.4-beta.2...v1.13.4-beta.3) (2025-08-04)


### Bug Fixes

* rework how RPC are called ([0f3c4f1](https://github.com/PurrNet/PurrNet/commit/0f3c4f1cfe992a89ca719afe53fd6e167c840d72))

## [1.13.4-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.13.4-beta.1...v1.13.4-beta.2) (2025-08-04)


### Bug Fixes

* Statistics manager versioning position fix ([1539c54](https://github.com/PurrNet/PurrNet/commit/1539c54a46659045893b82665387e52c6bfaca51))

## [1.13.4-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.13.3...v1.13.4-beta.1) (2025-08-03)


### Bug Fixes

* more robust register calling and skipping of assemblies that don't refrence the purrnet assembly ([5daec62](https://github.com/PurrNet/PurrNet/commit/5daec625ecb0c3cb405162afc2bcdb772f170d81))

## [1.13.3](https://github.com/PurrNet/PurrNet/compare/v1.13.2...v1.13.3) (2025-08-03)


### Bug Fixes

* build version info missing ([7befbe1](https://github.com/PurrNet/PurrNet/commit/7befbe1c4579a7ca3799d3d931a09860944af004))
* Dictionary pool domain reload safety added ([11c1c68](https://github.com/PurrNet/PurrNet/commit/11c1c68f366e955234e51730b1c35f5dc9d216dd))
* Merge pull request [#153](https://github.com/PurrNet/PurrNet/issues/153) from bookdude13/HasModule-Client-Fix ([b531534](https://github.com/PurrNet/PurrNet/commit/b5315344a4778626b039ea22fe7823bd9e74b834))
* packer caching problems ([878e7b9](https://github.com/PurrNet/PurrNet/commit/878e7b94b0389ec37b115b6c60f96ccc31a4f266))
* properly set scene as dirty ([15476e8](https://github.com/PurrNet/PurrNet/commit/15476e826b6a986dc51a1a9448b80e6a770b9943))
* version mismatch issue editor/build ([2ebe5a8](https://github.com/PurrNet/PurrNet/commit/2ebe5a8d841a3499fa9cb540ca1079f0fda48b4b))

## [1.13.3-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.13.3-beta.5...v1.13.3-beta.6) (2025-08-03)


### Bug Fixes

* version mismatch issue editor/build ([2ebe5a8](https://github.com/PurrNet/PurrNet/commit/2ebe5a8d841a3499fa9cb540ca1079f0fda48b4b))

## [1.13.3-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.13.3-beta.4...v1.13.3-beta.5) (2025-08-03)


### Bug Fixes

* properly set scene as dirty ([15476e8](https://github.com/PurrNet/PurrNet/commit/15476e826b6a986dc51a1a9448b80e6a770b9943))

## [1.13.3-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.13.3-beta.3...v1.13.3-beta.4) (2025-08-03)


### Bug Fixes

* build version info missing ([7befbe1](https://github.com/PurrNet/PurrNet/commit/7befbe1c4579a7ca3799d3d931a09860944af004))

## [1.13.3-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.13.3-beta.2...v1.13.3-beta.3) (2025-08-03)


### Bug Fixes

* packer caching problems ([878e7b9](https://github.com/PurrNet/PurrNet/commit/878e7b94b0389ec37b115b6c60f96ccc31a4f266))

## [1.13.3-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.13.3-beta.1...v1.13.3-beta.2) (2025-08-03)


### Bug Fixes

* Merge pull request [#153](https://github.com/PurrNet/PurrNet/issues/153) from bookdude13/HasModule-Client-Fix ([b531534](https://github.com/PurrNet/PurrNet/commit/b5315344a4778626b039ea22fe7823bd9e74b834))

## [1.13.3-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.13.2...v1.13.3-beta.1) (2025-08-02)


### Bug Fixes

* Dictionary pool domain reload safety added ([11c1c68](https://github.com/PurrNet/PurrNet/commit/11c1c68f366e955234e51730b1c35f5dc9d216dd))

## [1.13.2](https://github.com/PurrNet/PurrNet/compare/v1.13.1...v1.13.2) (2025-07-31)


### Bug Fixes

* handle the case where Transform is null when packing it ([51cc083](https://github.com/PurrNet/PurrNet/commit/51cc08347ac8da4c3fd361b455a5862f83d2c253))

## [1.13.2-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.13.1...v1.13.2-beta.1) (2025-07-31)


### Bug Fixes

* handle the case where Transform is null when packing it ([51cc083](https://github.com/PurrNet/PurrNet/commit/51cc08347ac8da4c3fd361b455a5862f83d2c253))

## [1.13.1](https://github.com/PurrNet/PurrNet/compare/v1.13.0...v1.13.1) (2025-07-31)


### Bug Fixes

* forceing release ([43af913](https://github.com/PurrNet/PurrNet/commit/43af913f3051249721557030abafbb926eec2ede))

## [1.13.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.13.0...v1.13.1-beta.1) (2025-07-31)


### Bug Fixes

* forceing release ([43af913](https://github.com/PurrNet/PurrNet/commit/43af913f3051249721557030abafbb926eec2ede))

# [2.0.0](https://github.com/PurrNet/PurrNet/compare/v1.12.4...v2.0.0) (2025-07-31)


### Bug Fixes

* `isIk` wasn't checking enough cases thx @OverGast ([4dd1c01](https://github.com/PurrNet/PurrNet/commit/4dd1c0133428365101c3bb28f087c9345ae0cc1e))
* add asServer for collider registration (rollback) ([92390cf](https://github.com/PurrNet/PurrNet/commit/92390cfda5053c55d005d28bd6c901ad6ee9af7b))
* Added connection UI example ([36be104](https://github.com/PurrNet/PurrNet/commit/36be104386c6237c5c6222a33b362906f30b4f32))
* Added create overloads for disposable types ([6650be1](https://github.com/PurrNet/PurrNet/commit/6650be1301666f0c87fa68d4a0bc6c7f0d9fcb4e))
* Added Disposable HashSet creation ([9f1b2e3](https://github.com/PurrNet/PurrNet/commit/9f1b2e3dfdf30cc56960b631dd56147ae7563671))
* Added proper asset post processing to network assets ([c10f7fe](https://github.com/PurrNet/PurrNet/commit/c10f7feade5ad191f5fea8b1d1a3f2d32a3f64ef))
* Additional safety added to packer of gameobject and transform ([20f3623](https://github.com/PurrNet/PurrNet/commit/20f36236b4d6929a8bf1956ae52175ce09ad7824))
* allow for manual despawning too ([0a01be8](https://github.com/PurrNet/PurrNet/commit/0a01be8322a43085da4a0e8b9a9f22de16033ee5))
* allow to dynamically register colliders for rollback history ([2d2762b](https://github.com/PurrNet/PurrNet/commit/2d2762b493afcc604c9e7e3e7fa3a24c50c42125))
* also render purrnet toolbar on clones ([c3dbb1c](https://github.com/PurrNet/PurrNet/commit/c3dbb1cd127403cba11f5a9c415a82e6679b38e4))
* attempt at fixing steam issue ([6c96b84](https://github.com/PurrNet/PurrNet/commit/6c96b8432ce3b6a94f29da7d01f06110bea646b4))
* attempt to circumvent caching ([de0c54b](https://github.com/PurrNet/PurrNet/commit/de0c54b1f1c33ab3c7fac9d607b2d97bf83699d0))
* Awaitable error on older versions ([7cd6ad9](https://github.com/PurrNet/PurrNet/commit/7cd6ad923ecfd4af2658728b2d0b337d8902b380))
* base writing replace old pointer ([23573a4](https://github.com/PurrNet/PurrNet/commit/23573a43b0e024f68c6694d323b33c7f07694bdf))
* Better button placement ([26d2e12](https://github.com/PurrNet/PurrNet/commit/26d2e12f883553d39ad82c7721bc4b6cf6541af1))
* better cancellation for purrtransport ([e7bbc5f](https://github.com/PurrNet/PurrNet/commit/e7bbc5f8b105edc0e81564900922f21858ebc6f2))
* better interface checking ([45cce33](https://github.com/PurrNet/PurrNet/commit/45cce33dd652e46113c1670911767211b720ea0c))
* Bitpacker updated for improved class handling ([9c42b92](https://github.com/PurrNet/PurrNet/commit/9c42b926e5404b7b5ed453fe31052a4467923549))
* BREAKING CHANGE fixed type in `AuthenticationBehaviour<T>`, renamed `GetClientPlayload` to `GetClientPayload` ([b03e333](https://github.com/PurrNet/PurrNet/commit/b03e333c40c3e637b67806041136c29df4ff3276))
* cleanup can run into destroyed identities ([539dd76](https://github.com/PurrNet/PurrNet/commit/539dd768b28e26c6db09ef676dd40e543ea66e62))
* Collider3DExtensions for other casting methods ([dfeac1f](https://github.com/PurrNet/PurrNet/commit/dfeac1f088ce9fb1f79d18d58fb43472fa2801d4))
* Compare synclist delta when receiving full state ([24aca2f](https://github.com/PurrNet/PurrNet/commit/24aca2f5efa6f3c4595e9baed3effe3561a5bc6f))
* Correct push ([a2bbc9b](https://github.com/PurrNet/PurrNet/commit/a2bbc9baa8c852c6bd1492df12c9f45e012da8f5))
* custom dela packer for NetworkID? is obsolete now ([353082c](https://github.com/PurrNet/PurrNet/commit/353082cbf300138b5f6d30dfd14d741e93fe3ab1))
* disposable list leak detection and GC reduction ([02be3c5](https://github.com/PurrNet/PurrNet/commit/02be3c5e8508d8eca16297f9288f9005ec3f8edc))
* disposing stuff ([6b74e68](https://github.com/PurrNet/PurrNet/commit/6b74e68801f1ca3667c26504b893482c82c35b63))
* dont use System.Threading.Tasks.Task.Yield due to webgl ([8c358bb](https://github.com/PurrNet/PurrNet/commit/8c358bb4739aa546a859cf28803553c2070329fb))
* Extended SyncVar callback to also include old value ([ffee19e](https://github.com/PurrNet/PurrNet/commit/ffee19ec610fb645ff97608bd718d9f854aa6267))
* for steam if localhost or local ip just connect to self ([43e9019](https://github.com/PurrNet/PurrNet/commit/43e9019e03e8efa916dc96abaa6d60c0b3fcbb3b))
* if parent type doesn't have a writer, use the specified type one ([e8df49a](https://github.com/PurrNet/PurrNet/commit/e8df49a1296e2082c3368d7fc60d4ccc1d026f2a))
* Improved purr buttons to work with inheritance ([d7363bb](https://github.com/PurrNet/PurrNet/commit/d7363bb889d5b75bc99d18ee75ec507f158becce))
* include Cache-Control header too ([86badfa](https://github.com/PurrNet/PurrNet/commit/86badfac77a023d7ca67aad322816fdca0ca0f70))
* include purrnet version and color buttons insteasd of showing LEDs ([9612890](https://github.com/PurrNet/PurrNet/commit/9612890fbee45da9f795ef4574894c25f9dcbefe))
* introduce `SetDirty` for syncvars ([dcd8f86](https://github.com/PurrNet/PurrNet/commit/dcd8f86d22a451d4128b5d3b5661e9a19e568c04))
* introduce LateLateUpdate for nt ([86c3d87](https://github.com/PurrNet/PurrNet/commit/86c3d87e49fce11e572261df6cbd6c22c8ec06d2))
* leak checker; removing some GC for rpcs ([3578dcf](https://github.com/PurrNet/PurrNet/commit/3578dcf1e6faee1a5c3eca086f406b15065fa98a))
* make sure client has the isSpawned boolean set to true ([568e256](https://github.com/PurrNet/PurrNet/commit/568e2563be49450e2339bfd61b7f10fd25cde4f4))
* make sure to apply the changed value ([83822be](https://github.com/PurrNet/PurrNet/commit/83822be32cef9a66ff712268291734ad2030e2d9))
* make sure we don't create something that is already registered ([78a6907](https://github.com/PurrNet/PurrNet/commit/78a69075603bf4248681989dbeba00edd0176898))
* make syncvar change existing value instead of creating a new one ([e9a7336](https://github.com/PurrNet/PurrNet/commit/e9a7336e1d8ecdb36c2ba420113158ee20eeb9eb))
* more purr transport tweaks ([a6da989](https://github.com/PurrNet/PurrNet/commit/a6da9895d9f511fb00566d4afaaa0cadbb562498))
* more raycast types for rollback module ([975ab10](https://github.com/PurrNet/PurrNet/commit/975ab103da67a36097f36517ec6255e96f9f6a83))
* move retry logic to purrtransport api level ([5d209a8](https://github.com/PurrNet/PurrNet/commit/5d209a8942838cbc797a3fa6e0bb85baaefc2759))
* Network assets post asset processing proper push ([b383377](https://github.com/PurrNet/PurrNet/commit/b3833779d77bbc2ab3b23e78960c7cebd53db359))
* NetworkAssetsEditor and null assets ([c30cc95](https://github.com/PurrNet/PurrNet/commit/c30cc95decee22b1cbd4825b77584b55725ece1a))
* Packer handling of unspawned gameobjects and transforms ([cc68315](https://github.com/PurrNet/PurrNet/commit/cc6831536deabda40ee8f7cce69d204692ab78fb))
* packer rework ([9630787](https://github.com/PurrNet/PurrNet/commit/9630787b9ba57066fd59cf84673d777d2ef756db))
* populate local player id as soon as server has it ([7fddf9d](https://github.com/PurrNet/PurrNet/commit/7fddf9dde5de0b03edd729ce3fb021b97c69567d))
* push `IsRegistered` ([b72a193](https://github.com/PurrNet/PurrNet/commit/b72a1931cf3cbe922a058c0bfd41cb4a58cae197))
* Quick stupid fix ([8804efe](https://github.com/PurrNet/PurrNet/commit/8804efed49cc42de997b7dc66f2923d64dde4bd1))
* remove readonly from ApplyTo method ([b3a0d13](https://github.com/PurrNet/PurrNet/commit/b3a0d131c731061a3c284caeb76ca03b4384fe8e))
* rename rollback methods and further test them ([5f10efd](https://github.com/PurrNet/PurrNet/commit/5f10efd7fa8f4e2ce3694cc755d4e03202bd69b1))
* retry for purr transport if first fails ([8330de0](https://github.com/PurrNet/PurrNet/commit/8330de02f989757d0d10c6855dce717c3166a90c))
* Scene objects spawn issue for HOST ([6cf0b02](https://github.com/PurrNet/PurrNet/commit/6cf0b0209b02fb50f20e5d2f1f926f5d99c56a15))
* simplify generic logic ([2a48bf3](https://github.com/PurrNet/PurrNet/commit/2a48bf37b8af52891d69508af835e46d29951dee))
* skip deep processing of certain assemblies ([6fe1411](https://github.com/PurrNet/PurrNet/commit/6fe1411d39b54221f168a80f26b335e9e5153063))
* some missed cases for dispose here ([1e751ee](https://github.com/PurrNet/PurrNet/commit/1e751ee8c278f6b936fa5ef713027c4ccd817d14))
* some serialization intricacies ([d8973f9](https://github.com/PurrNet/PurrNet/commit/d8973f9d0833793bb153c0fe69cd634c2c0c00e4))
* stopping steam server didn't properly close existing client connections ([ea36cb5](https://github.com/PurrNet/PurrNet/commit/ea36cb5e883ab159fd2866ab5f12c4ca8638a84f))
* syncvar let client decide instead of server for ownerauth stuff ([5b4cb65](https://github.com/PurrNet/PurrNet/commit/5b4cb65e423378f28e6e228832a5e2d3a18ea73a))
* trigger OnEarlySpawn when catching up ([9443c97](https://github.com/PurrNet/PurrNet/commit/9443c97afb637a497bbbc3e0ed11b8d1993f2f73))
* try to be more careful with errors here ([3beb8d5](https://github.com/PurrNet/PurrNet/commit/3beb8d548a90d3ab5f2d9b3d7644f7eeacaaa624))
* tuples were breaking code stripping ([2ec1406](https://github.com/PurrNet/PurrNet/commit/2ec14060bafb072877facca8b3949d475d292f1c))
* undo early client id setting as it was incorrect ([285268b](https://github.com/PurrNet/PurrNet/commit/285268b7390c2d9c9affe09dd04180f4b1fcb3b2))
* undo serialization order of base type ([d8c8560](https://github.com/PurrNet/PurrNet/commit/d8c85601e8f5f886a24d999e453c1c8bc5732e3f))
* use unscaledDeltaTime for NetworkTransform.cs ([77c23c9](https://github.com/PurrNet/PurrNet/commit/77c23c9bfc3279c435cc665e4db9f4bd2fae9172))
* webgl builds ([4dccfa5](https://github.com/PurrNet/PurrNet/commit/4dccfa56f567a24f881b14585082a0eb29113bc7))
* when adding connection make sure it's a new ID ([a61f451](https://github.com/PurrNet/PurrNet/commit/a61f4511b519e0d65af8e57b63973358c92e3bfd))


### Continuous Integration

* **release:** 1.13.0-beta.31 [skip ci] ([b1a396c](https://github.com/PurrNet/PurrNet/commit/b1a396c72e2313680f9adbc7ca46add33be67282))


### Features

* add toolbar display settings ([f289470](https://github.com/PurrNet/PurrNet/commit/f289470cb3f40623bc434c16afd79b4fc9cd98a7))
* client/server purrnet version missmatch checker ([3387274](https://github.com/PurrNet/PurrNet/commit/3387274f24a8e1e9a33aaf502d3e81afc6d35b4d))
* Copy my SteamID to clipboard ([8d504e4](https://github.com/PurrNet/PurrNet/commit/8d504e43a9df6d5c5da622b61457362d7730782a))
* Enable Pool Debug menu item ([c53c455](https://github.com/PurrNet/PurrNet/commit/c53c455b5265b74fd1a46e0975e1b505d7457b10))
* introduce `RawNetManager` ([59aa743](https://github.com/PurrNet/PurrNet/commit/59aa743f1f366431135b0846ceb8c63ddbad4937))
* introduce api to HierarchyV2 module that allows to manually manage spawning and observability events for lower level control ([9825580](https://github.com/PurrNet/PurrNet/commit/982558000c56142ed472b205e38a6a96e4aff96e))
* spawn validator for client spawning ([569ef7a](https://github.com/PurrNet/PurrNet/commit/569ef7a38a6b136f13d725ac993162d547e51e51))


### BREAKING CHANGES

* **release:** fixed type in `AuthenticationBehaviour<T>`, renamed `GetClientPlayload` to `GetClientPayload` ([b03e333](https://github.com/PurrNet/PurrNet/commit/b03e333c40c3e637b67806041136c29df4ff3276))

# [1.13.0-beta.62](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.61...v1.13.0-beta.62) (2025-07-31)


### Bug Fixes

* disposing stuff ([6b74e68](https://github.com/PurrNet/PurrNet/commit/6b74e68801f1ca3667c26504b893482c82c35b63))

# [1.13.0-beta.61](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.60...v1.13.0-beta.61) (2025-07-31)


### Bug Fixes

* push `IsRegistered` ([b72a193](https://github.com/PurrNet/PurrNet/commit/b72a1931cf3cbe922a058c0bfd41cb4a58cae197))

# [1.13.0-beta.60](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.59...v1.13.0-beta.60) (2025-07-31)


### Bug Fixes

* if parent type doesn't have a writer, use the specified type one ([e8df49a](https://github.com/PurrNet/PurrNet/commit/e8df49a1296e2082c3368d7fc60d4ccc1d026f2a))

# [1.13.0-beta.59](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.58...v1.13.0-beta.59) (2025-07-31)


### Features

* Enable Pool Debug menu item ([c53c455](https://github.com/PurrNet/PurrNet/commit/c53c455b5265b74fd1a46e0975e1b505d7457b10))

# [1.13.0-beta.58](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.57...v1.13.0-beta.58) (2025-07-31)


### Features

* client/server purrnet version missmatch checker ([3387274](https://github.com/PurrNet/PurrNet/commit/3387274f24a8e1e9a33aaf502d3e81afc6d35b4d))

# [1.13.0-beta.57](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.56...v1.13.0-beta.57) (2025-07-30)


### Bug Fixes

* some missed cases for dispose here ([1e751ee](https://github.com/PurrNet/PurrNet/commit/1e751ee8c278f6b936fa5ef713027c4ccd817d14))

# [1.13.0-beta.56](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.55...v1.13.0-beta.56) (2025-07-30)


### Bug Fixes

* base writing replace old pointer ([23573a4](https://github.com/PurrNet/PurrNet/commit/23573a43b0e024f68c6694d323b33c7f07694bdf))

# [1.13.0-beta.55](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.54...v1.13.0-beta.55) (2025-07-30)


### Bug Fixes

* undo serialization order of base type ([d8c8560](https://github.com/PurrNet/PurrNet/commit/d8c85601e8f5f886a24d999e453c1c8bc5732e3f))

# [1.13.0-beta.54](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.53...v1.13.0-beta.54) (2025-07-30)


### Bug Fixes

* some serialization intricacies ([d8973f9](https://github.com/PurrNet/PurrNet/commit/d8973f9d0833793bb153c0fe69cd634c2c0c00e4))

# [1.13.0-beta.53](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.52...v1.13.0-beta.53) (2025-07-30)


### Bug Fixes

* disposable list leak detection and GC reduction ([02be3c5](https://github.com/PurrNet/PurrNet/commit/02be3c5e8508d8eca16297f9288f9005ec3f8edc))

# [1.13.0-beta.52](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.51...v1.13.0-beta.52) (2025-07-30)


### Bug Fixes

* leak checker; removing some GC for rpcs ([3578dcf](https://github.com/PurrNet/PurrNet/commit/3578dcf1e6faee1a5c3eca086f406b15065fa98a))

# [1.13.0-beta.51](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.50...v1.13.0-beta.51) (2025-07-30)


### Bug Fixes

* make syncvar change existing value instead of creating a new one ([e9a7336](https://github.com/PurrNet/PurrNet/commit/e9a7336e1d8ecdb36c2ba420113158ee20eeb9eb))

# [1.13.0-beta.50](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.49...v1.13.0-beta.50) (2025-07-30)


### Bug Fixes

* introduce `SetDirty` for syncvars ([dcd8f86](https://github.com/PurrNet/PurrNet/commit/dcd8f86d22a451d4128b5d3b5661e9a19e568c04))

# [1.13.0-beta.49](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.48...v1.13.0-beta.49) (2025-07-28)


### Bug Fixes

* Added Disposable HashSet creation ([9f1b2e3](https://github.com/PurrNet/PurrNet/commit/9f1b2e3dfdf30cc56960b631dd56147ae7563671))

# [1.13.0-beta.48](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.47...v1.13.0-beta.48) (2025-07-27)


### Bug Fixes

* use unscaledDeltaTime for NetworkTransform.cs ([77c23c9](https://github.com/PurrNet/PurrNet/commit/77c23c9bfc3279c435cc665e4db9f4bd2fae9172))

# [1.13.0-beta.47](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.46...v1.13.0-beta.47) (2025-07-27)


### Bug Fixes

* better interface checking ([45cce33](https://github.com/PurrNet/PurrNet/commit/45cce33dd652e46113c1670911767211b720ea0c))
* make sure to apply the changed value ([83822be](https://github.com/PurrNet/PurrNet/commit/83822be32cef9a66ff712268291734ad2030e2d9))
* packer rework ([9630787](https://github.com/PurrNet/PurrNet/commit/9630787b9ba57066fd59cf84673d777d2ef756db))
* simplify generic logic ([2a48bf3](https://github.com/PurrNet/PurrNet/commit/2a48bf37b8af52891d69508af835e46d29951dee))

# [1.13.0-beta.46](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.45...v1.13.0-beta.46) (2025-07-27)


### Bug Fixes

* Bitpacker updated for improved class handling ([9c42b92](https://github.com/PurrNet/PurrNet/commit/9c42b926e5404b7b5ed453fe31052a4467923549))
* Compare synclist delta when receiving full state ([24aca2f](https://github.com/PurrNet/PurrNet/commit/24aca2f5efa6f3c4595e9baed3effe3561a5bc6f))

# [1.13.0-beta.45](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.44...v1.13.0-beta.45) (2025-07-25)


### Bug Fixes

* Additional safety added to packer of gameobject and transform ([20f3623](https://github.com/PurrNet/PurrNet/commit/20f36236b4d6929a8bf1956ae52175ce09ad7824))

# [1.13.0-beta.44](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.43...v1.13.0-beta.44) (2025-07-25)


### Bug Fixes

* Packer handling of unspawned gameobjects and transforms ([cc68315](https://github.com/PurrNet/PurrNet/commit/cc6831536deabda40ee8f7cce69d204692ab78fb))

# [1.13.0-beta.43](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.42...v1.13.0-beta.43) (2025-07-24)


### Features

* introduce `RawNetManager` ([59aa743](https://github.com/PurrNet/PurrNet/commit/59aa743f1f366431135b0846ceb8c63ddbad4937))

# [1.13.0-beta.42](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.41...v1.13.0-beta.42) (2025-07-23)


### Bug Fixes

* remove readonly from ApplyTo method ([b3a0d13](https://github.com/PurrNet/PurrNet/commit/b3a0d131c731061a3c284caeb76ca03b4384fe8e))

# [1.13.0-beta.41](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.40...v1.13.0-beta.41) (2025-07-22)


### Bug Fixes

* include Cache-Control header too ([86badfa](https://github.com/PurrNet/PurrNet/commit/86badfac77a023d7ca67aad322816fdca0ca0f70))

# [1.13.0-beta.40](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.39...v1.13.0-beta.40) (2025-07-22)


### Bug Fixes

* attempt to circumvent caching ([de0c54b](https://github.com/PurrNet/PurrNet/commit/de0c54b1f1c33ab3c7fac9d607b2d97bf83699d0))

# [1.13.0-beta.39](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.38...v1.13.0-beta.39) (2025-07-22)


### Bug Fixes

* more purr transport tweaks ([a6da989](https://github.com/PurrNet/PurrNet/commit/a6da9895d9f511fb00566d4afaaa0cadbb562498))

# [1.13.0-beta.38](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.37...v1.13.0-beta.38) (2025-07-22)


### Bug Fixes

* better cancellation for purrtransport ([e7bbc5f](https://github.com/PurrNet/PurrNet/commit/e7bbc5f8b105edc0e81564900922f21858ebc6f2))

# [1.13.0-beta.37](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.36...v1.13.0-beta.37) (2025-07-22)


### Bug Fixes

* move retry logic to purrtransport api level ([5d209a8](https://github.com/PurrNet/PurrNet/commit/5d209a8942838cbc797a3fa6e0bb85baaefc2759))

# [1.13.0-beta.36](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.35...v1.13.0-beta.36) (2025-07-22)


### Bug Fixes

* webgl builds ([4dccfa5](https://github.com/PurrNet/PurrNet/commit/4dccfa56f567a24f881b14585082a0eb29113bc7))

# [1.13.0-beta.35](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.34...v1.13.0-beta.35) (2025-07-21)


### Bug Fixes

* attempt at fixing steam issue ([6c96b84](https://github.com/PurrNet/PurrNet/commit/6c96b8432ce3b6a94f29da7d01f06110bea646b4))

# [1.13.0-beta.34](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.33...v1.13.0-beta.34) (2025-07-21)


### Bug Fixes

* for steam if localhost or local ip just connect to self ([43e9019](https://github.com/PurrNet/PurrNet/commit/43e9019e03e8efa916dc96abaa6d60c0b3fcbb3b))

# [1.13.0-beta.33](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.32...v1.13.0-beta.33) (2025-07-21)


### Features

* Copy my SteamID to clipboard ([8d504e4](https://github.com/PurrNet/PurrNet/commit/8d504e43a9df6d5c5da622b61457362d7730782a))

# [1.13.0-beta.32](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.31...v1.13.0-beta.32) (2025-07-21)


### Bug Fixes

* retry for purr transport if first fails ([8330de0](https://github.com/PurrNet/PurrNet/commit/8330de02f989757d0d10c6855dce717c3166a90c))

# [1.13.0-beta.31](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.30...v1.13.0-beta.31) (2025-07-21)


### Bug Fixes

* BREAKING CHANGE fixed type in `AuthenticationBehaviour<T>`, renamed `GetClientPlayload` to `GetClientPayload` ([b03e333](https://github.com/PurrNet/PurrNet/commit/b03e333c40c3e637b67806041136c29df4ff3276))

# [1.13.0-beta.30](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.29...v1.13.0-beta.30) (2025-07-21)


### Bug Fixes

* undo early client id setting as it was incorrect ([285268b](https://github.com/PurrNet/PurrNet/commit/285268b7390c2d9c9affe09dd04180f4b1fcb3b2))

# [1.13.0-beta.29](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.28...v1.13.0-beta.29) (2025-07-19)


### Bug Fixes

* Better button placement ([26d2e12](https://github.com/PurrNet/PurrNet/commit/26d2e12f883553d39ad82c7721bc4b6cf6541af1))

# [1.13.0-beta.28](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.27...v1.13.0-beta.28) (2025-07-19)


### Bug Fixes

* Added connection UI example ([36be104](https://github.com/PurrNet/PurrNet/commit/36be104386c6237c5c6222a33b362906f30b4f32))

# [1.13.0-beta.27](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.26...v1.13.0-beta.27) (2025-07-17)


### Bug Fixes

* make sure client has the isSpawned boolean set to true ([568e256](https://github.com/PurrNet/PurrNet/commit/568e2563be49450e2339bfd61b7f10fd25cde4f4))

# [1.13.0-beta.26](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.25...v1.13.0-beta.26) (2025-07-17)


### Bug Fixes

* populate local player id as soon as server has it ([7fddf9d](https://github.com/PurrNet/PurrNet/commit/7fddf9dde5de0b03edd729ce3fb021b97c69567d))

# [1.13.0-beta.25](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.24...v1.13.0-beta.25) (2025-07-17)


### Bug Fixes

* `isIk` wasn't checking enough cases thx @OverGast ([4dd1c01](https://github.com/PurrNet/PurrNet/commit/4dd1c0133428365101c3bb28f087c9345ae0cc1e))

# [1.13.0-beta.24](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.23...v1.13.0-beta.24) (2025-07-17)


### Bug Fixes

* skip deep processing of certain assemblies ([6fe1411](https://github.com/PurrNet/PurrNet/commit/6fe1411d39b54221f168a80f26b335e9e5153063))

# [1.13.0-beta.23](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.22...v1.13.0-beta.23) (2025-07-17)


### Bug Fixes

* also render purrnet toolbar on clones ([c3dbb1c](https://github.com/PurrNet/PurrNet/commit/c3dbb1cd127403cba11f5a9c415a82e6679b38e4))

# [1.13.0-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.21...v1.13.0-beta.22) (2025-07-17)


### Features

* add toolbar display settings ([f289470](https://github.com/PurrNet/PurrNet/commit/f289470cb3f40623bc434c16afd79b4fc9cd98a7))

# [1.13.0-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.20...v1.13.0-beta.21) (2025-07-16)


### Bug Fixes

* NetworkAssetsEditor and null assets ([c30cc95](https://github.com/PurrNet/PurrNet/commit/c30cc95decee22b1cbd4825b77584b55725ece1a))

# [1.13.0-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.19...v1.13.0-beta.20) (2025-07-16)


### Bug Fixes

* include purrnet version and color buttons insteasd of showing LEDs ([9612890](https://github.com/PurrNet/PurrNet/commit/9612890fbee45da9f795ef4574894c25f9dcbefe))

# [1.13.0-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.18...v1.13.0-beta.19) (2025-07-16)


### Bug Fixes

* Awaitable error on older versions ([7cd6ad9](https://github.com/PurrNet/PurrNet/commit/7cd6ad923ecfd4af2658728b2d0b337d8902b380))

# [1.13.0-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.17...v1.13.0-beta.18) (2025-07-15)


### Bug Fixes

* Improved purr buttons to work with inheritance ([d7363bb](https://github.com/PurrNet/PurrNet/commit/d7363bb889d5b75bc99d18ee75ec507f158becce))

# [1.13.0-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.16...v1.13.0-beta.17) (2025-07-14)


### Bug Fixes

* Extended SyncVar callback to also include old value ([ffee19e](https://github.com/PurrNet/PurrNet/commit/ffee19ec610fb645ff97608bd718d9f854aa6267))

# [1.13.0-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.15...v1.13.0-beta.16) (2025-07-13)


### Bug Fixes

* syncvar let client decide instead of server for ownerauth stuff ([5b4cb65](https://github.com/PurrNet/PurrNet/commit/5b4cb65e423378f28e6e228832a5e2d3a18ea73a))

# [1.13.0-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.14...v1.13.0-beta.15) (2025-07-13)


### Bug Fixes

* Scene objects spawn issue for HOST ([6cf0b02](https://github.com/PurrNet/PurrNet/commit/6cf0b0209b02fb50f20e5d2f1f926f5d99c56a15))

# [1.13.0-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.13...v1.13.0-beta.14) (2025-07-12)


### Bug Fixes

* Correct push ([a2bbc9b](https://github.com/PurrNet/PurrNet/commit/a2bbc9baa8c852c6bd1492df12c9f45e012da8f5))
* Quick stupid fix ([8804efe](https://github.com/PurrNet/PurrNet/commit/8804efed49cc42de997b7dc66f2923d64dde4bd1))

# [1.13.0-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.12...v1.13.0-beta.13) (2025-07-12)


### Bug Fixes

* Added create overloads for disposable types ([6650be1](https://github.com/PurrNet/PurrNet/commit/6650be1301666f0c87fa68d4a0bc6c7f0d9fcb4e))

# [1.13.0-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.11...v1.13.0-beta.12) (2025-07-12)


### Bug Fixes

* tuples were breaking code stripping ([2ec1406](https://github.com/PurrNet/PurrNet/commit/2ec14060bafb072877facca8b3949d475d292f1c))

# [1.13.0-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.10...v1.13.0-beta.11) (2025-07-11)


### Bug Fixes

* Network assets post asset processing proper push ([b383377](https://github.com/PurrNet/PurrNet/commit/b3833779d77bbc2ab3b23e78960c7cebd53db359))

# [1.13.0-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.9...v1.13.0-beta.10) (2025-07-11)


### Bug Fixes

* Added proper asset post processing to network assets ([c10f7fe](https://github.com/PurrNet/PurrNet/commit/c10f7feade5ad191f5fea8b1d1a3f2d32a3f64ef))

# [1.13.0-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.8...v1.13.0-beta.9) (2025-07-11)


### Bug Fixes

* when adding connection make sure it's a new ID ([a61f451](https://github.com/PurrNet/PurrNet/commit/a61f4511b519e0d65af8e57b63973358c92e3bfd))

# [1.13.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.7...v1.13.0-beta.8) (2025-07-10)


### Bug Fixes

* custom dela packer for NetworkID? is obsolete now ([353082c](https://github.com/PurrNet/PurrNet/commit/353082cbf300138b5f6d30dfd14d741e93fe3ab1))
* make sure we don't create something that is already registered ([78a6907](https://github.com/PurrNet/PurrNet/commit/78a69075603bf4248681989dbeba00edd0176898))

# [1.13.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.6...v1.13.0-beta.7) (2025-07-10)


### Bug Fixes

* cleanup can run into destroyed identities ([539dd76](https://github.com/PurrNet/PurrNet/commit/539dd768b28e26c6db09ef676dd40e543ea66e62))

# [1.13.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.5...v1.13.0-beta.6) (2025-07-10)


### Bug Fixes

* allow for manual despawning too ([0a01be8](https://github.com/PurrNet/PurrNet/commit/0a01be8322a43085da4a0e8b9a9f22de16033ee5))

# [1.13.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.4...v1.13.0-beta.5) (2025-07-10)


### Features

* introduce api to HierarchyV2 module that allows to manually manage spawning and observability events for lower level control ([9825580](https://github.com/PurrNet/PurrNet/commit/982558000c56142ed472b205e38a6a96e4aff96e))

# [1.13.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.3...v1.13.0-beta.4) (2025-07-10)


### Bug Fixes

* stopping steam server didn't properly close existing client connections ([ea36cb5](https://github.com/PurrNet/PurrNet/commit/ea36cb5e883ab159fd2866ab5f12c4ca8638a84f))

# [1.13.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.2...v1.13.0-beta.3) (2025-07-09)


### Bug Fixes

* try to be more careful with errors here ([3beb8d5](https://github.com/PurrNet/PurrNet/commit/3beb8d548a90d3ab5f2d9b3d7644f7eeacaaa624))

# [1.13.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.1...v1.13.0-beta.2) (2025-07-08)


### Bug Fixes

* trigger OnEarlySpawn when catching up ([9443c97](https://github.com/PurrNet/PurrNet/commit/9443c97afb637a497bbbc3e0ed11b8d1993f2f73))

# [1.13.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.12.5-beta.7...v1.13.0-beta.1) (2025-07-08)


### Features

* spawn validator for client spawning ([569ef7a](https://github.com/PurrNet/PurrNet/commit/569ef7a38a6b136f13d725ac993162d547e51e51))

## [1.12.5-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.12.5-beta.6...v1.12.5-beta.7) (2025-07-08)


### Bug Fixes

* introduce LateLateUpdate for nt ([86c3d87](https://github.com/PurrNet/PurrNet/commit/86c3d87e49fce11e572261df6cbd6c22c8ec06d2))

## [1.12.5-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.12.5-beta.5...v1.12.5-beta.6) (2025-07-08)


### Bug Fixes

* rename rollback methods and further test them ([5f10efd](https://github.com/PurrNet/PurrNet/commit/5f10efd7fa8f4e2ce3694cc755d4e03202bd69b1))

## [1.12.5-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.12.5-beta.4...v1.12.5-beta.5) (2025-07-08)


### Bug Fixes

* more raycast types for rollback module ([975ab10](https://github.com/PurrNet/PurrNet/commit/975ab103da67a36097f36517ec6255e96f9f6a83))

## [1.12.5-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.12.5-beta.3...v1.12.5-beta.4) (2025-07-07)


### Bug Fixes

* Collider3DExtensions for other casting methods ([dfeac1f](https://github.com/PurrNet/PurrNet/commit/dfeac1f088ce9fb1f79d18d58fb43472fa2801d4))

## [1.12.5-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.12.5-beta.2...v1.12.5-beta.3) (2025-07-07)


### Bug Fixes

* add asServer for collider registration (rollback) ([92390cf](https://github.com/PurrNet/PurrNet/commit/92390cfda5053c55d005d28bd6c901ad6ee9af7b))

## [1.12.5-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.12.5-beta.1...v1.12.5-beta.2) (2025-07-07)


### Bug Fixes

* allow to dynamically register colliders for rollback history ([2d2762b](https://github.com/PurrNet/PurrNet/commit/2d2762b493afcc604c9e7e3e7fa3a24c50c42125))

## [1.12.5-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.12.4...v1.12.5-beta.1) (2025-07-07)


### Bug Fixes

* dont use System.Threading.Tasks.Task.Yield due to webgl ([8c358bb](https://github.com/PurrNet/PurrNet/commit/8c358bb4739aa546a859cf28803553c2070329fb))

## [1.12.4](https://github.com/PurrNet/PurrNet/compare/v1.12.3...v1.12.4) (2025-07-07)


### Bug Fixes

* Added disposable list static creation ([101cf00](https://github.com/PurrNet/PurrNet/commit/101cf009c2bf5157a4e6cdeba973d81c0e4b54f7))
* better fallback serializers for delta compression ([2276832](https://github.com/PurrNet/PurrNet/commit/2276832f3f2ade89177e0550663aa4964361cd67))
* delta packer for generic System.Object ([479b535](https://github.com/PurrNet/PurrNet/commit/479b5356a4e01273f638c4c38f8b2f5e3ebfe0db))
* diposable dic packer stuff again ([707fcb8](https://github.com/PurrNet/PurrNet/commit/707fcb86a080c0de3c07a934288b1bf140ae76db))
* disposable dic delta writer ([73b7561](https://github.com/PurrNet/PurrNet/commit/73b75611d35929139324ca707e164e3e7588f3e0))
* fallback reader for delta didnt use new object serializer ([9541da6](https://github.com/PurrNet/PurrNet/commit/9541da68f9f5d96567578cf439c7be6d650ccbb8))
* hide in hierarchy only ([da99e58](https://github.com/PurrNet/PurrNet/commit/da99e58c17221bef61364cb9940159cdf06512c7))
* introduce the `Create(capacity)` variant for DisposableList ([4d1fab3](https://github.com/PurrNet/PurrNet/commit/4d1fab33107353af379cae204924f2c59795bdf7))
* just dont process NuGetForUnity ([0140920](https://github.com/PurrNet/PurrNet/commit/0140920a19800fe4512210fdfb1f79e2660f35b3))
* more nuget tests ([a6d144d](https://github.com/PurrNet/PurrNet/commit/a6d144ddee1795ccc94d36fceb346746b956dfee))
* more test ([2b237cd](https://github.com/PurrNet/PurrNet/commit/2b237cde9c67e4f60b4c5415c11d8b811d331566))
* Network Asset also pull base class assets ([89b0d56](https://github.com/PurrNet/PurrNet/commit/89b0d567db0e02c35ff7d2a9e1b6a6705f584847))
* Network asset exclude editor namespace ([11b45f6](https://github.com/PurrNet/PurrNet/commit/11b45f67388ada773138a21c6e830a38cd20cf08))
* old value was wrong for dic delta packer ([539c760](https://github.com/PurrNet/PurrNet/commit/539c7607415c493a881e0d676c5f90d068cd41f8))
* possible fix for network reflection buld ([3bbf58e](https://github.com/PurrNet/PurrNet/commit/3bbf58e46da52d62add19f4fe10e78ad72052c85))
* revert ([f6ffe42](https://github.com/PurrNet/PurrNet/commit/f6ffe42e384224b925167df4f18c853cbd4c9bd3))
* rigidbody moving weirdly if pooled ([5cc8524](https://github.com/PurrNet/PurrNet/commit/5cc85245aabcb458a5b793eb6f1cde9b64424565))
* State machine double enter and exit fix ([1b5fbc8](https://github.com/PurrNet/PurrNet/commit/1b5fbc8b5a51ad6fa4ebf56711a8cd8b24b22cb5))
* trying to fix nuget package issues ([bbf83d6](https://github.com/PurrNet/PurrNet/commit/bbf83d699cb9c800dd709c97b560cbcaefd575b6))
* ulong delta packer ([01445ae](https://github.com/PurrNet/PurrNet/commit/01445ae5c0cd1ae2147337a6ee7d8eb90a4f51a0))

## [1.12.4-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.19...v1.12.4-beta.20) (2025-07-06)


### Bug Fixes

* State machine double enter and exit fix ([1b5fbc8](https://github.com/PurrNet/PurrNet/commit/1b5fbc8b5a51ad6fa4ebf56711a8cd8b24b22cb5))

## [1.12.4-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.18...v1.12.4-beta.19) (2025-07-03)


### Bug Fixes

* possible fix for network reflection buld ([3bbf58e](https://github.com/PurrNet/PurrNet/commit/3bbf58e46da52d62add19f4fe10e78ad72052c85))

## [1.12.4-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.17...v1.12.4-beta.18) (2025-07-03)


### Bug Fixes

* Network asset exclude editor namespace ([11b45f6](https://github.com/PurrNet/PurrNet/commit/11b45f67388ada773138a21c6e830a38cd20cf08))

## [1.12.4-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.16...v1.12.4-beta.17) (2025-07-03)


### Bug Fixes

* Network Asset also pull base class assets ([89b0d56](https://github.com/PurrNet/PurrNet/commit/89b0d567db0e02c35ff7d2a9e1b6a6705f584847))

## [1.12.4-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.15...v1.12.4-beta.16) (2025-07-01)


### Bug Fixes

* introduce the `Create(capacity)` variant for DisposableList ([4d1fab3](https://github.com/PurrNet/PurrNet/commit/4d1fab33107353af379cae204924f2c59795bdf7))

## [1.12.4-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.14...v1.12.4-beta.15) (2025-07-01)


### Bug Fixes

* rigidbody moving weirdly if pooled ([5cc8524](https://github.com/PurrNet/PurrNet/commit/5cc85245aabcb458a5b793eb6f1cde9b64424565))

## [1.12.4-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.13...v1.12.4-beta.14) (2025-07-01)


### Bug Fixes

* just dont process NuGetForUnity ([0140920](https://github.com/PurrNet/PurrNet/commit/0140920a19800fe4512210fdfb1f79e2660f35b3))

## [1.12.4-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.12...v1.12.4-beta.13) (2025-07-01)


### Bug Fixes

* revert ([f6ffe42](https://github.com/PurrNet/PurrNet/commit/f6ffe42e384224b925167df4f18c853cbd4c9bd3))

## [1.12.4-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.11...v1.12.4-beta.12) (2025-07-01)


### Bug Fixes

* more test ([2b237cd](https://github.com/PurrNet/PurrNet/commit/2b237cde9c67e4f60b4c5415c11d8b811d331566))

## [1.12.4-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.10...v1.12.4-beta.11) (2025-07-01)


### Bug Fixes

* more nuget tests ([a6d144d](https://github.com/PurrNet/PurrNet/commit/a6d144ddee1795ccc94d36fceb346746b956dfee))

## [1.12.4-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.9...v1.12.4-beta.10) (2025-07-01)


### Bug Fixes

* trying to fix nuget package issues ([bbf83d6](https://github.com/PurrNet/PurrNet/commit/bbf83d699cb9c800dd709c97b560cbcaefd575b6))

## [1.12.4-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.8...v1.12.4-beta.9) (2025-06-30)


### Bug Fixes

* fallback reader for delta didnt use new object serializer ([9541da6](https://github.com/PurrNet/PurrNet/commit/9541da68f9f5d96567578cf439c7be6d650ccbb8))

## [1.12.4-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.7...v1.12.4-beta.8) (2025-06-30)


### Bug Fixes

* Added disposable list static creation ([101cf00](https://github.com/PurrNet/PurrNet/commit/101cf009c2bf5157a4e6cdeba973d81c0e4b54f7))

## [1.12.4-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.6...v1.12.4-beta.7) (2025-06-30)


### Bug Fixes

* delta packer for generic System.Object ([479b535](https://github.com/PurrNet/PurrNet/commit/479b5356a4e01273f638c4c38f8b2f5e3ebfe0db))

## [1.12.4-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.5...v1.12.4-beta.6) (2025-06-30)


### Bug Fixes

* better fallback serializers for delta compression ([2276832](https://github.com/PurrNet/PurrNet/commit/2276832f3f2ade89177e0550663aa4964361cd67))

## [1.12.4-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.4...v1.12.4-beta.5) (2025-06-30)


### Bug Fixes

* ulong delta packer ([01445ae](https://github.com/PurrNet/PurrNet/commit/01445ae5c0cd1ae2147337a6ee7d8eb90a4f51a0))

## [1.12.4-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.3...v1.12.4-beta.4) (2025-06-30)


### Bug Fixes

* old value was wrong for dic delta packer ([539c760](https://github.com/PurrNet/PurrNet/commit/539c7607415c493a881e0d676c5f90d068cd41f8))

## [1.12.4-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.2...v1.12.4-beta.3) (2025-06-30)


### Bug Fixes

* diposable dic packer stuff again ([707fcb8](https://github.com/PurrNet/PurrNet/commit/707fcb86a080c0de3c07a934288b1bf140ae76db))

## [1.12.4-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.1...v1.12.4-beta.2) (2025-06-30)


### Bug Fixes

* disposable dic delta writer ([73b7561](https://github.com/PurrNet/PurrNet/commit/73b75611d35929139324ca707e164e3e7588f3e0))

## [1.12.4-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.12.3...v1.12.4-beta.1) (2025-06-29)


### Bug Fixes

* hide in hierarchy only ([da99e58](https://github.com/PurrNet/PurrNet/commit/da99e58c17221bef61364cb9940159cdf06512c7))

## [1.12.3](https://github.com/PurrNet/PurrNet/compare/v1.12.2...v1.12.3) (2025-06-28)


### Bug Fixes

* add DisposableDictionary along side it's pool ([baa2c99](https://github.com/PurrNet/PurrNet/commit/baa2c9962539222ae62a203499712db0285321ce))
* always prepare the hash for `System.Object` ([5315b82](https://github.com/PurrNet/PurrNet/commit/5315b823ccb6edc1119b640c669b41538e711c8e))
* introduce disposable dictionary delta packers ([086c701](https://github.com/PurrNet/PurrNet/commit/086c701df10263ce47423aaf4b8aa20b023d8f51))
* ping calculations ([cd7cfd7](https://github.com/PurrNet/PurrNet/commit/cd7cfd70c1427c0d58dfe5e3601dd58ff79d2cb8))
* records ([983728a](https://github.com/PurrNet/PurrNet/commit/983728a6befc13b375ae4b8e5bbde8ed63c2cdbe))
* Server Stats added to statistics manager ([37a49ec](https://github.com/PurrNet/PurrNet/commit/37a49ec0279393a6a5330d6407f1f57fdc8d286c))
* Statistics for steam transport ([c1c16ff](https://github.com/PurrNet/PurrNet/commit/c1c16fff1692dd56c0db009e468ac87970d11adf))
* still prefer to call empty constructor instead of always initializing it to 0 ([5c667ac](https://github.com/PurrNet/PurrNet/commit/5c667ace880ba56b0a7b2aeb01066fcb60330fe0))
* whitelist dirty wasn't being executed ([7bb9351](https://github.com/PurrNet/PurrNet/commit/7bb93511c5afe551dfa5c73efa29aaad5161120c))
* writer for Ray2D ([24587cd](https://github.com/PurrNet/PurrNet/commit/24587cd0ee263e44e04997e3d09626300691f2e4))

## [1.12.3-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.12.3-beta.8...v1.12.3-beta.9) (2025-06-28)


### Bug Fixes

* writer for Ray2D ([24587cd](https://github.com/PurrNet/PurrNet/commit/24587cd0ee263e44e04997e3d09626300691f2e4))

## [1.12.3-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.12.3-beta.7...v1.12.3-beta.8) (2025-06-28)


### Bug Fixes

* always prepare the hash for `System.Object` ([5315b82](https://github.com/PurrNet/PurrNet/commit/5315b823ccb6edc1119b640c669b41538e711c8e))

## [1.12.3-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.12.3-beta.6...v1.12.3-beta.7) (2025-06-28)


### Bug Fixes

* Server Stats added to statistics manager ([37a49ec](https://github.com/PurrNet/PurrNet/commit/37a49ec0279393a6a5330d6407f1f57fdc8d286c))

## [1.12.3-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.12.3-beta.5...v1.12.3-beta.6) (2025-06-28)


### Bug Fixes

* introduce disposable dictionary delta packers ([086c701](https://github.com/PurrNet/PurrNet/commit/086c701df10263ce47423aaf4b8aa20b023d8f51))

## [1.12.3-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.12.3-beta.4...v1.12.3-beta.5) (2025-06-28)


### Bug Fixes

* add DisposableDictionary along side it's pool ([baa2c99](https://github.com/PurrNet/PurrNet/commit/baa2c9962539222ae62a203499712db0285321ce))

## [1.12.3-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.12.3-beta.3...v1.12.3-beta.4) (2025-06-27)


### Bug Fixes

* ping calculations ([cd7cfd7](https://github.com/PurrNet/PurrNet/commit/cd7cfd70c1427c0d58dfe5e3601dd58ff79d2cb8))
* Statistics for steam transport ([c1c16ff](https://github.com/PurrNet/PurrNet/commit/c1c16fff1692dd56c0db009e468ac87970d11adf))

## [1.12.3-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.12.3-beta.2...v1.12.3-beta.3) (2025-06-27)


### Bug Fixes

* whitelist dirty wasn't being executed ([7bb9351](https://github.com/PurrNet/PurrNet/commit/7bb93511c5afe551dfa5c73efa29aaad5161120c))

## [1.12.3-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.12.3-beta.1...v1.12.3-beta.2) (2025-06-27)


### Bug Fixes

* still prefer to call empty constructor instead of always initializing it to 0 ([5c667ac](https://github.com/PurrNet/PurrNet/commit/5c667ace880ba56b0a7b2aeb01066fcb60330fe0))

## [1.12.3-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.12.2...v1.12.3-beta.1) (2025-06-27)


### Bug Fixes

* records ([983728a](https://github.com/PurrNet/PurrNet/commit/983728a6befc13b375ae4b8e5bbde8ed63c2cdbe))

## [1.12.2](https://github.com/PurrNet/PurrNet/compare/v1.12.1...v1.12.2) (2025-06-26)


### Bug Fixes

* boost IL processing performance ([7d32309](https://github.com/PurrNet/PurrNet/commit/7d32309df8c4f0cbf2951d806528df25ddde2c8e))
* composite transport ([4c84b41](https://github.com/PurrNet/PurrNet/commit/4c84b41640a817a6e01f4ba72d8d18af252dec03))
* do ownership stuff on early observer added ([e5724c6](https://github.com/PurrNet/PurrNet/commit/e5724c6d37a8c5dab40f6fe5cd21c7570deaa8c1))
* proper comparer ([a30043c](https://github.com/PurrNet/PurrNet/commit/a30043c802391a2b98ad65502e93d1012f7edef8))

## [1.12.2-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.12.2-beta.3...v1.12.2-beta.4) (2025-06-26)


### Bug Fixes

* composite transport ([4c84b41](https://github.com/PurrNet/PurrNet/commit/4c84b41640a817a6e01f4ba72d8d18af252dec03))

## [1.12.2-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.12.2-beta.2...v1.12.2-beta.3) (2025-06-26)


### Bug Fixes

* proper comparer ([a30043c](https://github.com/PurrNet/PurrNet/commit/a30043c802391a2b98ad65502e93d1012f7edef8))

## [1.12.2-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.12.2-beta.1...v1.12.2-beta.2) (2025-06-26)


### Bug Fixes

* boost IL processing performance ([7d32309](https://github.com/PurrNet/PurrNet/commit/7d32309df8c4f0cbf2951d806528df25ddde2c8e))

## [1.12.2-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.12.1...v1.12.2-beta.1) (2025-06-26)


### Bug Fixes

* do ownership stuff on early observer added ([e5724c6](https://github.com/PurrNet/PurrNet/commit/e5724c6d37a8c5dab40f6fe5cd21c7570deaa8c1))

## [1.12.1](https://github.com/PurrNet/PurrNet/compare/v1.12.0...v1.12.1) (2025-06-25)


### Bug Fixes

* check if networkAssets isnt null ([1038e1a](https://github.com/PurrNet/PurrNet/commit/1038e1a1e90af75a4b6de4bdac8888fdda06f2f5))

## [1.12.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.12.0...v1.12.1-beta.1) (2025-06-25)


### Bug Fixes

* check if networkAssets isnt null ([1038e1a](https://github.com/PurrNet/PurrNet/commit/1038e1a1e90af75a4b6de4bdac8888fdda06f2f5))

# [1.12.0](https://github.com/PurrNet/PurrNet/compare/v1.11.1...v1.12.0) (2025-06-25)


### Bug Fixes

* `GetSpawnedParent` can throw an error ([513ce28](https://github.com/PurrNet/PurrNet/commit/513ce2845c0bcc6ec06ed9ed9574219e32d58d41))
* actually call Optimize on network animator batch ([a53f8e3](https://github.com/PurrNet/PurrNet/commit/a53f8e327ea65cceb56ff09e0a884cda6c152a2c))
* add `ServerOnlyAttribute` ([747451a](https://github.com/PurrNet/PurrNet/commit/747451a54e121107f3a87f2d22238a9eca255e87))
* add a `AlwaysIncludeDontDestroyOnLoadScene` in the network rules ([9404b77](https://github.com/PurrNet/PurrNet/commit/9404b778a7fbbe5a18201886da52f4c7f3524be6))
* add onPreProcessRpc and onPostProcessRpc to the RPCModule ([c588685](https://github.com/PurrNet/PurrNet/commit/c5886856b03a774452fd0618e480baefa2bb0655))
* added a changelog ([13af73d](https://github.com/PurrNet/PurrNet/commit/13af73dceddb751b26a8d25f37d485fe79706a25))
* Allow to save bandwidth to file and then load it in the editor ([2117e33](https://github.com/PurrNet/PurrNet/commit/2117e3355b268ef455f5c56cc13d05612f33098c))
* always gen the rpc signature ([1afa09c](https://github.com/PurrNet/PurrNet/commit/1afa09c6e0c45971251da0f9a395bd281ed0074c))
* batch acks for delta module ([cc4c89d](https://github.com/PurrNet/PurrNet/commit/cc4c89dbe46d2c29e717a52d4968a0226dd5cfa5))
* better error for when sync modules miss permissions ([e28df7b](https://github.com/PurrNet/PurrNet/commit/e28df7b9587fcdf47a7ae799f0c4bb9bcda16920))
* better static generic type discovery ([da5f6e9](https://github.com/PurrNet/PurrNet/commit/da5f6e954ed4727c6f09034ab8291c0036f95a93))
* better visibility API ([3af2c32](https://github.com/PurrNet/PurrNet/commit/3af2c32f62426564feb14db552412c66ed8bfd84))
* BitPacker being in Write mode when received for Reading ([7ebb8aa](https://github.com/PurrNet/PurrNet/commit/7ebb8aa45a3fb6f3283f977da42ca44100f84c9f))
* change name of package for openupm ([b759197](https://github.com/PurrNet/PurrNet/commit/b759197c0a11986a029e7caf333d3fe44655e5da))
* copy managed types when calling RPCs locally ([28b7091](https://github.com/PurrNet/PurrNet/commit/28b70917a70429f84332b1acefcc82fedf6bf272))
* DontPackAttribute only works for field ([5846ecd](https://github.com/PurrNet/PurrNet/commit/5846ecd9a5c4f2d9a07e41361f64e67ac8ddb0ec))
* ensure that it at least replaces with empty method for `ServerOnly` ([9750c5d](https://github.com/PurrNet/PurrNet/commit/9750c5d620e05c10421c0f0578451285d58358eb))
* enum delta packers weren't implemented ([13ed11f](https://github.com/PurrNet/PurrNet/commit/13ed11f922651136ee52b3e7ab09a91c7ca52902))
* Expanded the rtt summary ([7668055](https://github.com/PurrNet/PurrNet/commit/766805521bacdba984a15deb9f8011aed71c78c5))
* if server, always use the ownerServer value ([9626f51](https://github.com/PurrNet/PurrNet/commit/9626f513957ec5db316e27807bc622786820879e))
* improved statistics manager ([8fed412](https://github.com/PurrNet/PurrNet/commit/8fed412172ffdb88d74d7b80c1d093052f10644c))
* include full type for generic too ([4990d69](https://github.com/PurrNet/PurrNet/commit/4990d6983b059c20252c9dafd80250c6b93824e0))
* introduced DontPack attribute ([2fea79e](https://github.com/PurrNet/PurrNet/commit/2fea79e8cc8e2598001e29ab73b51fe4feaf7eb9))
* LastNID patch, this needs to be reworked ([16dc6d3](https://github.com/PurrNet/PurrNet/commit/16dc6d30cec6c85eb8fad123be0a3bfee2299a5a))
* link the changlog ([9ef043a](https://github.com/PurrNet/PurrNet/commit/9ef043a70732867218d4aaf98f0d2e7c0c38fbf0))
* make core unity dependencies optional ([12b06e1](https://github.com/PurrNet/PurrNet/commit/12b06e191792bb7d1c7416621c2c500af044f935))
* metadata file for CHANGELOG.md ([dd139fc](https://github.com/PurrNet/PurrNet/commit/dd139fc066987c8942d8751d6f194a917fa9616c))
* missing using ([0f51df2](https://github.com/PurrNet/PurrNet/commit/0f51df2921e55dc28c483d4efe444267dc14fab5))
* Network assets pull multiple sub assets ([de49d8b](https://github.com/PurrNet/PurrNet/commit/de49d8b07fdabb9057336bfef4317c806e7d6357))
* Network Assets working with Sub-assets ([769ff32](https://github.com/PurrNet/PurrNet/commit/769ff32e111da0315d6c077c0e1c8e41902a8900))
* network reflection and network assets ([1adea71](https://github.com/PurrNet/PurrNet/commit/1adea71cf4a1517122a5130429500a4a99ece8fa))
* only keep latest `SetX` for animation ([badec0d](https://github.com/PurrNet/PurrNet/commit/badec0dd5b6f56b88085f4e1ea6195ff4a3d33cf))
* ownership events ([9a245f9](https://github.com/PurrNet/PurrNet/commit/9a245f9c7dd4a9a70da9daa2fd27c57db84b711f))
* properly populate RPCInfo for runlocally ([bd99145](https://github.com/PurrNet/PurrNet/commit/bd991450479f1b09bff4e2be463e9cfd8c9b567a))
* refactoring `AreEqual` helpers for the packer ([20b2c70](https://github.com/PurrNet/PurrNet/commit/20b2c70665be9960e6df05776ebe261e53a45c7b))
* remove UniTask as a dependency ([725cabf](https://github.com/PurrNet/PurrNet/commit/725cabfc54a037375e94fb16ccbcb2e1d94aead7))
* reverted bad changes ([94914f4](https://github.com/PurrNet/PurrNet/commit/94914f4b907105abf1f4646551d61210c706eff4))
* server rpc's on server should not use the network ([06b6d9d](https://github.com/PurrNet/PurrNet/commit/06b6d9d15a78c7b908367af60ffea1e1137b9115))
* set target frame rate to tick rate for server builds ([b1fc358](https://github.com/PurrNet/PurrNet/commit/b1fc35896b66e2ea69f13910962e1a82199787c7))
* start server/client, stop server/client always calls the network manager and does it through it instead of individually, otherwise things are unpredictable ([157d47c](https://github.com/PurrNet/PurrNet/commit/157d47cd8405893fd0180b9621f58fc3e6da788b))
* state machine editor issues in prefab runtime ([d0ad04a](https://github.com/PurrNet/PurrNet/commit/d0ad04a033fe5e0d860cdd11a6d1cd9be8a16c46))
* State machine exit on despawn ([9884c58](https://github.com/PurrNet/PurrNet/commit/9884c585b1aa8950b56fbc7db82d58d1039bc864))
* Statistics manager improvements ([f494ce9](https://github.com/PurrNet/PurrNet/commit/f494ce96b947ea8a69d049ed50adc39ab4432ac6))
* Statistics manager jitter ([0c5d611](https://github.com/PurrNet/PurrNet/commit/0c5d611b215a5d049c3494c58c189b3b5c4ff8b9))
* steam server not properly cleaning internal state ([af3a793](https://github.com/PurrNet/PurrNet/commit/af3a7932271bf7547e8d14bfc23a26e539aa3445))
* Sync dictionary sending for clients ([88ce60a](https://github.com/PurrNet/PurrNet/commit/88ce60a2f56e5d594a9f2c54b055eaef8790d4b9))
* Sync types for strict rules ([7722477](https://github.com/PurrNet/PurrNet/commit/7722477cba75fc22b49c6b23af70d4e4b5d57132))
* undo mess ([9f0f26c](https://github.com/PurrNet/PurrNet/commit/9f0f26c336b16ec78d6f340dd529286cf5c05fad))
* unityProxyType being null caused IL issues ([15a85cd](https://github.com/PurrNet/PurrNet/commit/15a85cd3b10ec0865965ad5fa190a68467879f3c))
* weird ownership order ([634ed88](https://github.com/PurrNet/PurrNet/commit/634ed88a8098049f9455cda503b0f5eb7cf7a96e))
* when sending a target rpc to local player just call it locally ([2982811](https://github.com/PurrNet/PurrNet/commit/2982811a01626b4f0cdf0da0378c5c25a26aa2ff))


### Features

* Network assets added ([16ebe3c](https://github.com/PurrNet/PurrNet/commit/16ebe3c4e91db8ab14f0d7c075294bae0354f33c))
* unity editor toolbar with purrnet state ([dbdb6cb](https://github.com/PurrNet/PurrNet/commit/dbdb6cb04ac88fb364826430c2a32273ad8e79b8))

# [1.12.0-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.10...v1.12.0-beta.11) (2025-06-25)


### Bug Fixes

* BitPacker being in Write mode when received for Reading ([7ebb8aa](https://github.com/PurrNet/PurrNet/commit/7ebb8aa45a3fb6f3283f977da42ca44100f84c9f))
* network reflection and network assets ([1adea71](https://github.com/PurrNet/PurrNet/commit/1adea71cf4a1517122a5130429500a4a99ece8fa))

# [1.12.0-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.9...v1.12.0-beta.10) (2025-06-24)


### Bug Fixes

* set target frame rate to tick rate for server builds ([b1fc358](https://github.com/PurrNet/PurrNet/commit/b1fc35896b66e2ea69f13910962e1a82199787c7))

# [1.12.0-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.8...v1.12.0-beta.9) (2025-06-24)


### Bug Fixes

* Network assets pull multiple sub assets ([de49d8b](https://github.com/PurrNet/PurrNet/commit/de49d8b07fdabb9057336bfef4317c806e7d6357))

# [1.12.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.7...v1.12.0-beta.8) (2025-06-24)


### Bug Fixes

* Network Assets working with Sub-assets ([769ff32](https://github.com/PurrNet/PurrNet/commit/769ff32e111da0315d6c077c0e1c8e41902a8900))

# [1.12.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.6...v1.12.0-beta.7) (2025-06-24)


### Bug Fixes

* ownership events ([9a245f9](https://github.com/PurrNet/PurrNet/commit/9a245f9c7dd4a9a70da9daa2fd27c57db84b711f))

# [1.12.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.5...v1.12.0-beta.6) (2025-06-23)


### Bug Fixes

* Statistics manager jitter ([0c5d611](https://github.com/PurrNet/PurrNet/commit/0c5d611b215a5d049c3494c58c189b3b5c4ff8b9))

# [1.12.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.4...v1.12.0-beta.5) (2025-06-22)


### Bug Fixes

* include full type for generic too ([4990d69](https://github.com/PurrNet/PurrNet/commit/4990d6983b059c20252c9dafd80250c6b93824e0))

# [1.12.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.3...v1.12.0-beta.4) (2025-06-22)


### Bug Fixes

* better static generic type discovery ([da5f6e9](https://github.com/PurrNet/PurrNet/commit/da5f6e954ed4727c6f09034ab8291c0036f95a93))

# [1.12.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.2...v1.12.0-beta.3) (2025-06-22)


### Bug Fixes

* Expanded the rtt summary ([7668055](https://github.com/PurrNet/PurrNet/commit/766805521bacdba984a15deb9f8011aed71c78c5))

# [1.12.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.1...v1.12.0-beta.2) (2025-06-22)


### Features

* unity editor toolbar with purrnet state ([dbdb6cb](https://github.com/PurrNet/PurrNet/commit/dbdb6cb04ac88fb364826430c2a32273ad8e79b8))

# [1.12.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.11.2-beta.41...v1.12.0-beta.1) (2025-06-20)


### Features

* Network assets added ([16ebe3c](https://github.com/PurrNet/PurrNet/commit/16ebe3c4e91db8ab14f0d7c075294bae0354f33c))

## [1.11.2-beta.41](https://github.com/PurrNet/PurrNet/compare/v1.11.2-beta.40...v1.11.2-beta.41) (2025-06-20)


### Bug Fixes

* weird ownership order ([634ed88](https://github.com/PurrNet/PurrNet/commit/634ed88a8098049f9455cda503b0f5eb7cf7a96e))

## [1.11.2-beta.40](https://github.com/PurrNet/PurrNet/compare/v1.11.2-beta.39...v1.11.2-beta.40) (2025-06-20)


### Bug Fixes

* link the changlog ([9ef043a](https://github.com/PurrNet/PurrNet/commit/9ef043a70732867218d4aaf98f0d2e7c0c38fbf0))

## [1.11.2-beta.39](https://github.com/PurrNet/PurrNet/compare/v1.11.2-beta.38...v1.11.2-beta.39) (2025-06-20)


### Bug Fixes

* metadata file for CHANGELOG.md ([dd139fc](https://github.com/PurrNet/PurrNet/commit/dd139fc066987c8942d8751d6f194a917fa9616c))

## [1.11.2-beta.38](https://github.com/PurrNet/PurrNet/compare/v1.11.2-beta.37...v1.11.2-beta.38) (2025-06-20)


### Bug Fixes

* added a changelog ([13af73d](https://github.com/PurrNet/PurrNet/commit/13af73dceddb751b26a8d25f37d485fe79706a25))

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

<!-- This section will be automatically populated by semantic-release -->

<!--
## [1.0.0] - YYYY-MM-DD
### Added
- New features

### Changed
- Changes in existing functionality

### Deprecated
- Soon-to-be removed features

### Removed
- Removed features

### Fixed
- Bug fixes

### Security
- Vulnerability fixes
-->
