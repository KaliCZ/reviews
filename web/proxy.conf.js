module.exports = {
  '/api': {
    target: process.env.API_URL || 'http://localhost:5146',
    secure: false,
    changeOrigin: true,
    logLevel: 'debug'
  }
};
