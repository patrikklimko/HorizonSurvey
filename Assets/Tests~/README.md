# Tests

This folder ends in `~`, so Unity ignores it entirely.

The Unity Test Framework package was removed from this project along with the
Engineering feature set (see week 1 blog post - a corporate TLS proxy made
package resolution unreliable on the work network).

To enable these tests:

1. On a network without TLS interception, open Unity and go to
   Window > Package Manager > + > Install package by name
   and install `com.unity.test-framework`.
2. Rename this folder from `Tests~` to `Tests`.
3. Window > General > Test Runner > EditMode > Run All.

Until then, `Horizon Survey > Run Solar Self-Check` in the menu bar covers the
same ground without needing the package.
