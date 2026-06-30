/**
 * Session Timeout Handler
 * Automatically handles session expiration and redirects to login
 * Include this in your main layout file
 */

(function () {
    'use strict';

    // Session timeout configuration (match server-side timeout)
    const SESSION_TIMEOUT_MINUTES = 30;
    const WARNING_BEFORE_TIMEOUT_MINUTES = 5;
    const CHECK_INTERVAL_MS = 60000; // Check every minute

    let lastActivity = new Date();
    let timeoutWarningShown = false;

    // Update last activity time
    function updateActivity() {
        lastActivity = new Date();
        timeoutWarningShown = false;
    }

    // Check session timeout
    function checkSessionTimeout() {
        const now = new Date();
        const inactiveMinutes = (now - lastActivity) / 1000 / 60;

        // Show warning before timeout
        if (inactiveMinutes >= (SESSION_TIMEOUT_MINUTES - WARNING_BEFORE_TIMEOUT_MINUTES) && !timeoutWarningShown) {
            timeoutWarningShown = true;

            if (typeof Swal !== 'undefined') {
                Swal.fire({
                    title: 'Session Expiring Soon',
                    text: `Your session will expire in ${WARNING_BEFORE_TIMEOUT_MINUTES} minutes due to inactivity.`,
                    icon: 'warning',
                    confirmButtonText: 'Stay Logged In',
                    confirmButtonColor: '#3085d6'
                }).then((result) => {
                    if (result.isConfirmed) {
                        updateActivity();
                        // Make a lightweight request to keep session alive
                        fetch('/Auth/KeepAlive', { method: 'POST' })
                            .catch(() => { }); // Ignore errors
                    }
                });
            }
        }

        // Session expired
        if (inactiveMinutes >= SESSION_TIMEOUT_MINUTES) {
            handleSessionExpired();
        }
    }

    // Handle session expiration
    function handleSessionExpired() {
        if (typeof Swal !== 'undefined') {
            Swal.fire({
                title: 'Session Expired',
                text: 'Your session has expired. Please login again.',
                icon: 'error',
                confirmButtonText: 'Login',
                confirmButtonColor: '#3085d6',
                allowOutsideClick: false
            }).then(() => {
                window.location.href = '/Auth/Login';
            });
        } else {
            alert('Your session has expired. Please login again.');
            window.location.href = '/Auth/Login';
        }
    }

    // Track user activity
    const activityEvents = ['mousedown', 'keydown', 'scroll', 'touchstart', 'click'];
    activityEvents.forEach(event => {
        document.addEventListener(event, updateActivity, true);
    });

    // Global AJAX error handler for 401 (Unauthorized)
    if (typeof $ !== 'undefined' && $.ajaxSetup) {
        $(document).ajaxError(function (event, jqxhr, settings, thrownError) {
            if (jqxhr.status === 401) {
                const response = jqxhr.responseJSON;
                if (response && response.redirect) {
                    if (typeof Swal !== 'undefined') {
                        Swal.fire({
                            title: 'Session Expired',
                            text: response.message || 'Your session has expired. Please login again.',
                            icon: 'error',
                            confirmButtonText: 'Login',
                            confirmButtonColor: '#3085d6',
                            allowOutsideClick: false
                        }).then(() => {
                            window.location.href = response.redirect;
                        });
                    } else {
                        window.location.href = response.redirect;
                    }
                }
            }
        });
    }

    // Start timeout checker
    setInterval(checkSessionTimeout, CHECK_INTERVAL_MS);

    console.log('Session timeout handler initialized');
})();