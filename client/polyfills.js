// Ensure Web Crypto API is available as globalThis.crypto for older Node versions
try {
  if (typeof globalThis.crypto === 'undefined') {
    const maybe = require('crypto')
    // prefer webcrypto when available
    if (maybe.webcrypto) {
      globalThis.crypto = maybe.webcrypto
    } else {
      globalThis.crypto = {}
    }
  }

  // Ensure getRandomValues exists (some libs expect this exact function)
  if (typeof globalThis.crypto.getRandomValues !== 'function') {
    const { randomBytes } = require('crypto')
    globalThis.crypto.getRandomValues = function (array) {
      if (!(array && typeof array.length === 'number')) {
        throw new TypeError('Expected an array-like object')
      }
      const buf = randomBytes(array.length)
      if (array.set) {
        array.set(buf)
      } else {
        for (let i = 0; i < buf.length; i++) array[i] = buf[i]
      }
      return array
    }
  }
} catch (e) {
  // ignore - if this fails the error will be surfaced by Vite later
}
