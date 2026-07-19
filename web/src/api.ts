import wretch from 'wretch';

// Shared wretch client rooted at the API prefix. Paths passed to `.url()`
// should start with `/` (e.g. api.url('/health')). Non-2xx responses reject
// with a WretchError whose message is the response body, so callers can
// surface server-provided errors from a catch block.
export const api = wretch('/api');
