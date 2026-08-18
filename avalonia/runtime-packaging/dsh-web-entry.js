#!/usr/bin/env node
/**
 * Packaged Windows runtime entry for the dsh Web UI. This module delegates to
 * the built dsh CLI bundled beside it (`./lib/bin.js`), so the runtime needs no
 * system Node, no pnpm, and never runs `pnpm dsh web`. Arguments after this
 * entry are forwarded to the CLI unchanged, e.g. `web --port 0`.
 */

import './lib/bin.js'