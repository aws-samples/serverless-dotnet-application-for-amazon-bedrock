// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

function generate_fm_params() {
    var params = {};

    $('.bedrock-options').each(function () {
        if ($(this).val() != null) {
            params[$(this).attr('id')] = $(this).val();
        }
    });

    return params;
}

function add_message_inline(p, msg) {
    $(p).append(msg);
}

$("#option_temperature").on("input change", function () {
    $('#current_temperature').html($(this).val());
});
$("#option_top_p").on("input change", function () {
    $('#current_top_p').html($(this).val());
});