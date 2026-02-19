import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react-swc'
import { webcrypto } from 'crypto'

if (typeof globalThis.crypto === 'undefined') {
  // Provide Web Crypto API for environments (Node 16) where it's not available globally
  // Vite and some deps expect `globalThis.crypto.getRandomValues`
  // eslint-disable-next-line @typescript-eslint/ban-ts-comment
  // @ts-ignore
  globalThis.crypto = webcrypto
}

// Disable reading external Babel config from filesystem to avoid duplicate
// or conflicting `@babel/core` loads which can trigger "Cannot redefine property: File".
export default defineConfig({
  plugins: [react()]
})
