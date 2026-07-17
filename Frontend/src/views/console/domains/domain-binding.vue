<template>
  <div class="p-24">
    <div class="flex items-center pb-24 space-x-16">
      <el-button round @click="addDialog.open">
        <div class="flex items-center">
          {{ t("common.new") }}
        </div>
      </el-button>
    </div>

    <KTable :data="list" show-check @delete="onDelete">
      <el-table-column :label="t('common.domain')">
        <template #default="{ row }">
          <span>{{ row.fullName }}</span>
        </template>
      </el-table-column>
      <el-table-column :label="t('common.site')">
        <template #default="{ row }">
          <a
            class="cursor-pointer text-blue"
            :href="`/_Admin/site?SiteId=${row.webSiteId}`"
            target="_blank"
            data-cy="site-name"
          >
            {{ row.siteName }}
          </a>
        </template>
      </el-table-column>
      <el-table-column :label="t('common.SSLEnabled')">
        <template #default="{ row }">
          <div class="flex items-center">
            <el-tooltip
              class="box-item"
              effect="dark"
              :content="row.enableSsl ? '' : t('common.enableSSL')"
              :disabled="row.enableSsl"
              placement="top"
            >
              <el-switch
                :model-value="row.enableSsl"
                :disabled="row.enableSsl"
                data-cy="ssl-enabled"
                @update:model-value="onEnableSSL(row)"
              />
            </el-tooltip>

            <ElTag v-if="row.sslError" round type="danger" class="ml-4"
              >{{ t("common.failed") }}
              <Tooltip :tip="row.sslError" custom-class="ml-4" />
            </ElTag>
          </div>
        </template>
      </el-table-column>
      <el-table-column width="150px" align="center">
        <template #default="{ row }">
          <el-button type="primary" round @click="preview(row.fullName)">{{
            t("common.preview")
          }}</el-button>
        </template>
      </el-table-column>
    </KTable>

    <AddDialog
      v-model="addDialog.status"
      :domain="domain!"
      @create-success="load"
    />
  </div>
</template>

<script lang="ts" setup>
import AddDialog from "./domain-binding-dialog.vue";
import { ref, reactive } from "vue";
import KTable from "@/components/k-table";
import { useI18n } from "vue-i18n";
import type { Domain, ListByDomain } from "@/api/console/types";
import { getQueryString } from "@/utils/url";
import {
  getDomain,
  getBindingListByDomain,
  deleteBinding,
} from "@/api/console";
import { openInNewTab } from "@/utils/url";
import { showDeleteConfirm } from "@/components/basic/confirm";
import { verifySSL, setSsl } from "@/api/binding";

const onEnableSSL = async (row: any) => {
  try {
    await verifySSL({
      rootDomain: row.fullName,
    });

    await setSsl({
      rootDomain: row.fullName,
    });
  } catch (error) {
    //
  }

  load();
};

const { t } = useI18n();

const addDialog = reactive({
  status: false,
  open() {
    addDialog.status = true;
  },
});
const preview = (url: string) => {
  openInNewTab("//" + url);
};

const domain = ref<Domain>();
const list = ref<ListByDomain[]>();

async function load() {
  const id = getQueryString("id");
  if (id) {
    list.value = await getBindingListByDomain(id);
    domain.value = await getDomain(id);
  }
}

load();

async function onDelete(list: ListByDomain[]) {
  if (list.length) {
    await showDeleteConfirm(list.length);
    await deleteBinding(list.map((item) => item.id));
    load();
  }
}
</script>
