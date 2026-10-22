# Stats for Windows

Optional C# / WPF metrics publisher for the Seelen desktop-suite adaptation.
Version 1.0.5 source; AGPL-3.0-or-later for original component code.

It supplies Windows CPU/GPU/memory/disk/network/battery and optional sensor metrics,
configuration, bounded history, alert policies and detail popovers. Unsupported
hardware data is unavailable, not fabricated. macOS Stats inspired the module model;
this is not a port of Apple's APIs or the macOS app's collector.

Seelen is the primary host, lifecycle and settings entry. Stats is independently
buildable and optional. Legacy standalone settings are a recovery/development path,
not a request to recreate the old Brimbar-based suite.

See [component build, test and license notes](../README.md).
