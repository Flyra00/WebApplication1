(function () {
    'use strict';

    const COOKIE_TABLE = 'nr_tableNumber';
    const COOKIE_MEMBER = 'nr_membershipStatus';


    function setCookie(name, value, days) {
        const d = new Date();
        d.setTime(d.getTime() + (days * 24 * 60 * 60 * 1000));
        const expires = 'expires=' + d.toUTCString();
        document.cookie = name + '=' + encodeURIComponent(value) + ';' + expires + ';path=/;SameSite=Lax';
    }

    function getCookie(name) {
        const nameEq = name + '=';
        const parts = document.cookie.split(';');
        for (let i = 0; i < parts.length; i++) {
            let c = parts[i].trim();
            if (c.indexOf(nameEq) === 0) return decodeURIComponent(c.substring(nameEq.length, c.length));
        }
        return null;
    }

    function deleteCookie(name) {
        document.cookie = name + '=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/; SameSite=Lax';
    }

    function setBadge(tableNumber) {
        const badgeValue = document.getElementById('tableBadgeValue');
        if (!badgeValue) return;
        badgeValue.textContent = (tableNumber && String(tableNumber).trim() !== '') ? tableNumber : '-';
    }

    function getSelectedMembership() {
        const checked = document.querySelector('input[name="membershipStatus"]:checked');
        return checked ? checked.value : 'Guest';
    }

    function setSelectedMembership(value) {
        const v = (value === 'Member') ? 'Member' : 'Guest';
        const input = document.querySelector('input[name="membershipStatus"][value="' + v + '"]');
        if (input) input.checked = true;
    }

    function showModal(modalEl) {
        if (!modalEl || typeof bootstrap === 'undefined') return;
        const instance = bootstrap.Modal.getOrCreateInstance(modalEl, { backdrop: 'static' });
        instance.show();
    }

    function hideModal(modalEl) {
        if (!modalEl || typeof bootstrap === 'undefined') return;
        const instance = bootstrap.Modal.getOrCreateInstance(modalEl);
        instance.hide();
    }

    document.addEventListener('DOMContentLoaded', function () {
        const isAdminPage = (document.body && document.body.dataset && document.body.dataset.adminpage === 'true');

        const modalEl = document.getElementById('tableModal');
        const tableInput = document.getElementById('tableNumberInput');
        const saveBtn = document.getElementById('tableSaveBtn');
        const resetBtn = document.getElementById('tableResetBtn');


        const existingTable = getCookie(COOKIE_TABLE);
        const existingMembership = getCookie(COOKIE_MEMBER);
        if (!isAdminPage) {
            setBadge(existingTable);
            if (existingMembership) setSelectedMembership(existingMembership);
        }


        if (!isAdminPage && modalEl) {
            modalEl.addEventListener('show.bs.modal', function () {
                if (tableInput) tableInput.value = '';
                setSelectedMembership(getCookie(COOKIE_MEMBER) || 'Guest');
            });
        }


        if (!isAdminPage && !existingTable) {
            showModal(modalEl);
        }

        if (!isAdminPage && saveBtn) {
            saveBtn.addEventListener('click', function () {
                const val = tableInput ? String(tableInput.value).trim() : '';
                if (!val || isNaN(Number(val)) || Number(val) <= 0) {
                    if (tableInput) {
                        tableInput.classList.add('is-invalid');
                        tableInput.focus();
                    }
                    return;
                }
                if (tableInput) tableInput.classList.remove('is-invalid');

                const member = getSelectedMembership();
                setCookie(COOKIE_TABLE, val, 365);
                setCookie(COOKIE_MEMBER, member, 365);

                setBadge(val);

                // Jika Member dipilih, tampilkan Login/Register modal setelah saving (jika belum login)
                if (member === 'Member' && window.nrIsAuthenticated !== true) {
                    const authEl = document.getElementById('authModal');

                    if (authEl && modalEl) {
                        const onHidden = function () {
                            modalEl.removeEventListener('hidden.bs.modal', onHidden);
                            showModal(authEl);
                        };
                        modalEl.addEventListener('hidden.bs.modal', onHidden);
                    }
                }

                hideModal(modalEl);
            });
        }

        if (!isAdminPage && resetBtn) {
            resetBtn.addEventListener('click', function () {
                deleteCookie(COOKIE_TABLE);
                deleteCookie(COOKIE_MEMBER);

                if (tableInput) {
                    tableInput.value = '';
                    tableInput.classList.remove('is-invalid');
                    tableInput.focus();
                }
                setSelectedMembership('Guest');
                setBadge(null);
            });
        }



        // Member auth modal controls (Login/Register)
        const authModalEl = document.getElementById('authModal');
        const authContinueBtn = document.getElementById('authContinueBtn');
        const loginUsername = document.getElementById('loginUsername');
        const loginPassword = document.getElementById('loginPassword');
        const regName = document.getElementById('regName');
        const regUsername = document.getElementById('regUsername');
        const regPassword = document.getElementById('regPassword');

        function clearInvalid(el) {
            if (!el) return;
            el.classList.remove('is-invalid');
        }

        function requireValue(el) {
            if (!el) return true;
            const v = String(el.value || '').trim();
            if (!v) {
                el.classList.add('is-invalid');
                el.focus();
                return false;
            }
            el.classList.remove('is-invalid');
            return true;
        }

        if (authModalEl) {
            authModalEl.addEventListener('show.bs.modal', function () {

                [loginUsername, loginPassword, regName, regUsername, regPassword].forEach(clearInvalid);
            });
        }

        // Toggle password visibility (eye icon)
        document.querySelectorAll('[data-toggle-password]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                const sel = btn.getAttribute('data-toggle-password');
                const input = sel ? document.querySelector(sel) : null;
                if (!input) return;

                const isHidden = (input.type === 'password');
                input.type = isHidden ? 'text' : 'password';

                const icon = btn.querySelector('i');
                if (icon) {
                    icon.classList.toggle('bi-eye', !isHidden);
                    icon.classList.toggle('bi-eye-slash', isHidden);
                }
            });
        });

        async function postJson(url, payload) {
            const res = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: JSON.stringify(payload)
            });
            return await res.json();
        }

        if (authContinueBtn) {
            authContinueBtn.addEventListener('click', function () {
                const activePane = document.querySelector('#authTabContent .tab-pane.active');
                if (!activePane) return;

                if (activePane.id === 'login-pane') {
                    const ok = requireValue(loginUsername) && requireValue(loginPassword);
                    if (!ok) return;

                    postJson('/Auth/AjaxLogin', {
                        username: String(loginUsername.value || '').trim(),
                        password: String(loginPassword.value || '')
                    }).then(function (data) {
                        if (data && data.success) {
                            hideModal(authModalEl);
                            // refresh supaya navbar berubah 
                            window.location.href = data.redirectUrl || window.location.href;
                            return;
                        }
                        // tampilkan error
                        loginPassword.classList.add('is-invalid');
                    }).catch(function () {
                        loginPassword.classList.add('is-invalid');
                    });
                    return;
                }

                if (activePane.id === 'register-pane') {
                    const ok = requireValue(regName) && requireValue(regUsername) && requireValue(regPassword);
                    if (!ok) return;

                    postJson('/Auth/AjaxRegister', {
                        fullName: String(regName.value || '').trim(),
                        username: String(regUsername.value || '').trim(),
                        password: String(regPassword.value || '')
                    }).then(function (data) {
                        if (data && data.success) {
                            hideModal(authModalEl);
                            window.location.href = data.redirectUrl || window.location.href;
                            return;
                        }
                        regPassword.classList.add('is-invalid');
                    }).catch(function () {
                        regPassword.classList.add('is-invalid');
                    });
                    return;
                }
            });
        }


        [loginUsername, loginPassword, regName, regUsername, regPassword].forEach(function (el) {
            if (!el) return;
            el.addEventListener('input', function () { el.classList.remove('is-invalid'); });
        });

        if (!isAdminPage && tableInput) {
            tableInput.addEventListener('input', function () {
                tableInput.classList.remove('is-invalid');
            });
        }
    });
})();
