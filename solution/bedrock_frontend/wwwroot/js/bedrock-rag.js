// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

function generate_fm_params() {
    var params = {};

    $('.bedrock-options').each(function () {
        if ($(this).val() != null) {
            params[$(this).attr('id')] = $(this).val();
        }
    });

    if ($('#option_rag_session').val().length > 0) {
        params["rag_session"] = $('#option_rag_session').val();
    }

    return params;
}

function add_message_inline(p, msg) {
    $(p).append(msg);
}