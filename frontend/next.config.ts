import type { NextConfig } from "next";

// Server-side only. There is deliberately no NEXT_PUBLIC_ prefix: the browser
// bundle must never contain the backend's address.

function resolveBackendOrigin(): string {
  const raw = process.env.BACKEND_ORIGIN;
  if (!raw) {
    throw new Error("BACKEND_ORIGIN is not set, put it in frontend/.env.local; on Vercel, add it to the project settings > Environment Variables.");
  }

let url: URL;
try {
  url = new URL(raw);
} catch {
  throw new Error(`BACKEND_ORIGIN is not a valid URL: ${raw}`);
}
if (url.protocol !== "http:" && url.protocol !== "https:") {
  throw new Error(`BACKEND_ORIGIN must be an http or https URL, got: ${raw}`);
}

return url.origin; //also drops any trailing slash
}

const backendOrigin = resolveBackendOrigin();

const nextConfig: NextConfig = {
  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${backendOrigin}/:path*`,
      },
    ];
  },
};

export default nextConfig;