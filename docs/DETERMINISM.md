# Determinism (Phase 0)

- Shared PRNG: `XorShift32` with explicit serialized state.
- No runtime-dependent RNG from shared sim.
- World hashing helper (`WorldHash`) included for Phase 1 replay tests.
- Phase 1 harness validates:
  - protocol snapshot roundtrip
  - input command roundtrip including sequence + client tick
  - same seed => same random stream
  - deterministic movement replay:
    - same seed + same generated input stream => same final world hash
- Phase 3 slice adds deterministic combat assertions:
  - fast attack builds builder resource
  - heavy attack spends spender resource
  - block state mitigates incoming enemy damage
  - enemy death spawns loot and pickup grants currency
