(function () {
    'use strict';

    const COOKIE_TABLE = 'nr_tableNumber';
    const COOKIE_TABLE_TOKEN = 'nr_tableToken';
    const COOKIE_MEMBER = 'nr_membershipStatus';
    const CART_STORAGE_KEY = 'nr_orderCart';


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

    function saveCart(cart) {
        localStorage.setItem(CART_STORAGE_KEY, JSON.stringify(cart));
    }

    function loadCart() {
        try {
            const raw = localStorage.getItem(CART_STORAGE_KEY);
            if (!raw) return [];

            const parsed = JSON.parse(raw);
            return Array.isArray(parsed) ? parsed : [];
        } catch {
            return [];
        }
    }

    function clearCart() {
        localStorage.removeItem(CART_STORAGE_KEY);
    }

    function formatCurrency(value) {
        return new Intl.NumberFormat('id-ID', {
            style: 'currency',
            currency: 'IDR',
            minimumFractionDigits: 0,
            maximumFractionDigits: 0
        }).format(value || 0);
    }

    function escapeHtml(value) {
        return String(value || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
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
        const addToCartButtons = document.querySelectorAll('.js-add-to-cart');
        const cartItemsEl = document.getElementById('orderCartItems');
        const cartEmptyEl = document.getElementById('orderCartEmpty');
        const cartTotalEl = document.getElementById('orderCartTotal');
        const submitOrderBtn = document.getElementById('submitOrderBtn');
        const orderFeedbackEl = document.getElementById('orderFeedback');
        let cart = loadCart();


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
                deleteCookie(COOKIE_TABLE_TOKEN);

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
                deleteCookie(COOKIE_TABLE_TOKEN);
                deleteCookie(COOKIE_MEMBER);
                clearCart();
                cart = [];

                if (tableInput) {
                    tableInput.value = '';
                    tableInput.classList.remove('is-invalid');
                    tableInput.focus();
                }
                setSelectedMembership('Guest');
                setBadge(null);
                renderCart();
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

        function setOrderFeedback(message, type) {
            if (!orderFeedbackEl) return;

            orderFeedbackEl.className = 'small mt-3';
            orderFeedbackEl.textContent = '';

            if (!message) return;

            orderFeedbackEl.textContent = message;
            orderFeedbackEl.classList.add(type === 'success' ? 'text-success' : 'text-danger');
        }

        function renderCart() {
            if (!cartItemsEl || !cartEmptyEl || !cartTotalEl || !submitOrderBtn) return;

            if (cart.length === 0) {
                cartEmptyEl.style.display = '';
                cartItemsEl.innerHTML = '';
                cartTotalEl.textContent = formatCurrency(0);
                submitOrderBtn.disabled = true;
                return;
            }

            cartEmptyEl.style.display = 'none';
            submitOrderBtn.disabled = false;

            let total = 0;
            cartItemsEl.innerHTML = cart.map(function (item) {
                const lineTotal = Number(item.price) * Number(item.qty);
                total += lineTotal;

                return [
                    '<div class="list-group-item px-0">',
                    '<div class="d-flex justify-content-between align-items-start gap-3">',
                    '<div>',
                    '<div class="fw-semibold">' + escapeHtml(item.name) + '</div>',
                    '<div class="text-muted small">' + formatCurrency(item.price) + '</div>',
                    '</div>',
                    '<div class="d-flex align-items-center gap-2">',
                    '<button type="button" class="btn btn-sm btn-outline-secondary" data-cart-action="decrease" data-product-id="' + item.productId + '">-</button>',
                    '<span class="fw-semibold">' + item.qty + '</span>',
                    '<button type="button" class="btn btn-sm btn-outline-secondary" data-cart-action="increase" data-product-id="' + item.productId + '">+</button>',
                    '<button type="button" class="btn btn-sm btn-outline-danger" data-cart-action="remove" data-product-id="' + item.productId + '">x</button>',
                    '</div>',
                    '</div>',
                    '<div class="text-end fw-bold mt-2">' + formatCurrency(lineTotal) + '</div>',
                    '</div>'
                ].join('');
            }).join('');

            cartTotalEl.textContent = formatCurrency(total);
        }

        function upsertCartItem(item) {
            const existing = cart.find(function (cartItem) {
                return Number(cartItem.productId) === Number(item.productId);
            });

            if (existing) {
                existing.qty += 1;
            } else {
                cart.push({
                    productId: Number(item.productId),
                    name: item.name,
                    price: Number(item.price),
                    qty: 1
                });
            }

            saveCart(cart);
            renderCart();
            setOrderFeedback('Menu ditambahkan ke pesanan.', 'success');
        }

        addToCartButtons.forEach(function (button) {
            button.addEventListener('click', function () {
                upsertCartItem({
                    productId: button.dataset.productId,
                    name: button.dataset.productName || 'Menu',
                    price: button.dataset.productPrice || '0'
                });
            });
        });

        if (cartItemsEl) {
            cartItemsEl.addEventListener('click', function (event) {
                const target = event.target.closest('[data-cart-action]');
                if (!target) return;

                const action = target.getAttribute('data-cart-action');
                const productId = Number(target.getAttribute('data-product-id'));
                const item = cart.find(function (cartItem) {
                    return Number(cartItem.productId) === productId;
                });

                if (!item) return;

                if (action === 'increase') item.qty += 1;
                if (action === 'decrease') item.qty -= 1;
                if (action === 'remove' || item.qty <= 0) {
                    cart = cart.filter(function (cartItem) {
                        return Number(cartItem.productId) !== productId;
                    });
                }

                saveCart(cart);
                renderCart();
                setOrderFeedback('', '');
            });
        }

        if (submitOrderBtn) {
            submitOrderBtn.addEventListener('click', function () {
                const tableNumber = getCookie(COOKIE_TABLE);
                const tableToken = getCookie(COOKIE_TABLE_TOKEN);
                const membershipStatus = getCookie(COOKIE_MEMBER) || 'Guest';

                if (!tableNumber) {
                    setOrderFeedback('Pilih nomor meja dulu sebelum memesan.', 'error');
                    showModal(modalEl);
                    return;
                }

                if (membershipStatus === 'Member' && window.nrIsAuthenticated !== true) {
                    setOrderFeedback('Login member dulu sebelum mengirim pesanan.', 'error');
                    showModal(document.getElementById('authModal'));
                    return;
                }

                if (cart.length === 0) {
                    setOrderFeedback('Pesanan masih kosong.', 'error');
                    return;
                }

                submitOrderBtn.disabled = true;
                setOrderFeedback('Mengirim pesanan...', 'success');

                postJson('/CustomerOrder/Submit', {
                    tableNumber: Number(tableNumber),
                    tableToken: tableToken || null,
                    membershipStatus: membershipStatus,
                    items: cart.map(function (item) {
                        return {
                            productId: Number(item.productId),
                            qty: Number(item.qty)
                        };
                    })
                }).then(function (data) {
                    if (!data || !data.success) {
                        submitOrderBtn.disabled = false;
                        setOrderFeedback((data && data.error) || 'Pesanan gagal dikirim.', 'error');
                        renderCart();
                        return;
                    }

                    cart = [];
                    clearCart();
                    renderCart();
                    setOrderFeedback('Pesanan berhasil dikirim. Nomor order: ' + data.orderNumber + '. Total: Rp' + data.total + '.', 'success');
                }).catch(function () {
                    submitOrderBtn.disabled = false;
                    setOrderFeedback('Terjadi error saat mengirim pesanan.', 'error');
                    renderCart();
                });
            });
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

        renderCart();
    });
})();
